using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Doccer.TestRunner;

internal sealed class TestRunInfrastructureException : Exception
{
    public TestRunInfrastructureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class TestRunCompletion : IDisposable
{
    private TestRunReservation? _reservation;

    public TestRunCompletion(
        TestRunReceipt receipt,
        TestArtifactLayout artifacts,
        TestRunReservation reservation)
    {
        Receipt = receipt;
        Artifacts = artifacts;
        _reservation = reservation;
    }

    public TestRunReceipt Receipt { get; }

    public TestArtifactLayout Artifacts { get; }

    public void Dispose() =>
        Interlocked.Exchange(ref _reservation, null)?.Dispose();
}

internal static class TestRunCoordinator
{
    private const long MaximumPlanBytes = 1024 * 1024;

    public static async Task<TestRunCompletion> ExecuteAsync(
        TestRunOptions options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var plan = await LoadPlanAsync(options).ConfigureAwait(false);
        TestPlanExpansion expansion;
        try
        {
            expansion = await TestPlanExpander.ExpandAsync(
                    plan,
                    options.RepositoryRoot,
                    options.Configuration,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TestPlanExpansionValidationException exception)
        {
            throw new TestRunnerUsageException(exception.Message, exception);
        }
        catch (Exception exception)
        {
            throw new TestRunInfrastructureException(
                $"The test plan could not be expanded: " +
                TestRunDiagnostics.BoundMessage(exception.Message),
                exception);
        }

        var workItems = expansion.WorkItems;
        var requestedAtUtc = timeProvider.GetUtcNow();
        TestRunReservation reservation;
        try
        {
            reservation = TestRunReservation.Create(
                options.RepositoryRoot,
                requestedAtUtc,
                workItems);
        }
        catch (Exception exception)
        {
            throw new TestRunInfrastructureException(
                $"The repository-local test run could not be reserved: " +
                TestRunDiagnostics.BoundMessage(exception.Message),
                exception);
        }

        var ownershipTransferred = false;
        try
        {
            var completion = await ExecuteReservedAsync(
                    options,
                    workItems,
                    reservation,
                    requestedAtUtc,
                    timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
            ownershipTransferred = true;
            return completion;
        }
        catch (Exception exception) when (exception is not TestRunnerUsageException)
        {
            var relativeRunPath = Path.GetRelativePath(
                    options.RepositoryRoot,
                    reservation.Artifacts.RunDirectory)
                .Replace('\\', '/');
            throw new TestRunInfrastructureException(
                $"Test run '{relativeRunPath}' could not be finalized: " +
                TestRunDiagnostics.BoundMessage(exception.Message),
                exception);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                reservation.Dispose();
            }
        }
    }

    private static async Task<TestRunCompletion> ExecuteReservedAsync(
        TestRunOptions options,
        IReadOnlyList<TestWorkItem> workItems,
        TestRunReservation reservation,
        DateTimeOffset requestedAtUtc,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var artifacts = reservation.Artifacts;
        var caseLayouts = artifacts.Cases.ToDictionary(
            layout => layout.WorkItemId,
            StringComparer.Ordinal);
        var planSnapshot = TestRunPlanSnapshot.Create(
            reservation.RunId,
            requestedAtUtc,
            options.Configuration,
            options.MaxParallel,
            workItems,
            artifacts);
        await using var evidence = await RunEvidenceWriter.CreateAsync(
                artifacts,
                planSnapshot)
            .ConfigureAwait(false);

        var runStartedAtUtc = timeProvider.GetUtcNow();
        await evidence.AppendEventAsync(
                reservation.RunId,
                runStartedAtUtc,
                TestRunEventKind.RunStarted)
            .ConfigureAwait(false);
        using var cancellationEvidence = new RunCancellationEvidence(
            evidence,
            reservation.RunId,
            timeProvider,
            cancellationToken);

        var results = await BoundedTestScheduler.RunAsync(
                workItems,
                options.MaxParallel,
                (workItem, executionToken) => ExecuteScheduledItemAsync(
                    options.RepositoryRoot,
                    reservation.RunId,
                    workItem,
                    artifacts,
                    caseLayouts[workItem.Id],
                    evidence,
                    cancellationEvidence,
                    timeProvider,
                    executionToken),
                workItem => AccountForNotStartedAsync(
                    reservation.RunId,
                    workItem,
                    caseLayouts[workItem.Id],
                    evidence,
                    cancellationEvidence,
                    timeProvider),
                cancellationToken)
            .ConfigureAwait(false);

        var runCompletedAtUtc = timeProvider.GetUtcNow();
        await evidence.AppendEventAsync(
                reservation.RunId,
                runCompletedAtUtc,
                TestRunEventKind.RunFinished)
            .ConfigureAwait(false);
        var summary = TestRunSummary.Create(
            planSnapshot,
            runStartedAtUtc,
            runCompletedAtUtc,
            results);
        await evidence.WriteSummaryAsync(summary).ConfigureAwait(false);
        var prunedRunCount = FinalizedRunPruner.Prune(artifacts, summary);
        var receipt = TestRunReceipt.Create(summary, artifacts, prunedRunCount);
        return new TestRunCompletion(receipt, artifacts, reservation);
    }

    private static async Task<TestExecutionResult> ExecuteScheduledItemAsync(
        string repositoryRoot,
        string runId,
        TestWorkItem workItem,
        TestArtifactLayout artifacts,
        TestCaseArtifactLayout caseLayout,
        RunEvidenceWriter evidence,
        RunCancellationEvidence cancellationEvidence,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return await AccountForNotStartedAsync(
                    runId,
                    workItem,
                    caseLayout,
                    evidence,
                    cancellationEvidence,
                    timeProvider)
                .ConfigureAwait(false);
        }

        var itemStartedAtUtc = timeProvider.GetUtcNow();
        await evidence.AppendEventAsync(
                runId,
                itemStartedAtUtc,
                TestRunEventKind.ItemStarted,
                workItem.Id)
            .ConfigureAwait(false);
        var outcome = await SingleProcessExecutor.ExecuteAsync(
                repositoryRoot,
                runId,
                workItem,
                artifacts,
                caseLayout,
                itemStartedAtUtc,
                timeProvider,
                cancellationToken)
            .ConfigureAwait(false);
        if (outcome.CancellationRequestedAtUtc is { } cancellationRequestedAtUtc)
        {
            await cancellationEvidence.EnsureWrittenAsync(cancellationRequestedAtUtc)
                .ConfigureAwait(false);
        }

        var finalized = WorkItemFinalizer.Finalize(
            runId,
            workItem,
            caseLayout,
            outcome,
            timeProvider);
        await evidence.AppendEventAsync(
                runId,
                timeProvider.GetUtcNow(),
                TestRunEventKind.ItemFinished,
                workItem.Id,
                finalized.Result.Status)
            .ConfigureAwait(false);
        await evidence.MaterializeCaseAsync(
                caseLayout,
                finalized.Result,
                finalized.StandardOutput,
                finalized.StandardError)
            .ConfigureAwait(false);
        return finalized.Result;
    }

    private static async Task<TestExecutionResult> AccountForNotStartedAsync(
        string runId,
        TestWorkItem workItem,
        TestCaseArtifactLayout caseLayout,
        RunEvidenceWriter evidence,
        RunCancellationEvidence cancellationEvidence,
        TimeProvider timeProvider)
    {
        await cancellationEvidence.EnsureWrittenAsync(timeProvider.GetUtcNow())
            .ConfigureAwait(false);
        var result = TestExecutionResult.Create(
            runId,
            workItem.Id,
            TestExecutionStatus.NotStarted,
            errorType: "canceled_before_start",
            errorMessage: "Cancellation was requested before the child process started.");
        await evidence.AppendEventAsync(
                runId,
                timeProvider.GetUtcNow(),
                TestRunEventKind.ItemFinished,
                workItem.Id,
                result.Status)
            .ConfigureAwait(false);
        await evidence.MaterializeCaseAsync(
                caseLayout,
                result,
                EmptyStream(),
                EmptyStream())
            .ConfigureAwait(false);
        return result;
    }

    private static CapturedProcessStream EmptyStream() =>
        new(Evidence: null, Content: null);

    private static async Task<TestPlan> LoadPlanAsync(TestRunOptions options)
    {
        try
        {
            RepositoryEntryGuard.RequireExistingWithoutReparsePoints(
                options.RepositoryRoot,
                options.PlanPath,
                directory: false,
                nameof(options.PlanPath));
            await using var stream = new FileStream(
                options.PlanPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaximumPlanBytes)
            {
                throw new TestRunnerUsageException(
                    $"The test plan cannot exceed {MaximumPlanBytes} bytes.");
            }

            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 16 * 1024,
                leaveOpen: true);
            var json = await reader.ReadToEndAsync().ConfigureAwait(false);
            return TestPlanParser.Parse(json, options.RepositoryRoot);
        }
        catch (TestRunnerUsageException)
        {
            throw;
        }
        catch (TestPlanValidationException exception)
        {
            throw new TestRunnerUsageException(exception.Message, exception);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            DecoderFallbackException or
            NotSupportedException or
            ArgumentException)
        {
            throw new TestRunnerUsageException(
                $"The test plan could not be loaded: " +
                TestRunDiagnostics.BoundMessage(exception.Message),
                exception);
        }
    }

    private sealed class RunCancellationEvidence : IDisposable
    {
        private readonly object _sync = new();
        private readonly RunEvidenceWriter _evidence;
        private readonly string _runId;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly CancellationTokenRegistration _registration;
        private DateTimeOffset? _requestedAtUtc;
        private bool _written;

        public RunCancellationEvidence(
            RunEvidenceWriter evidence,
            string runId,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            _evidence = evidence;
            _runId = runId;
            _timeProvider = timeProvider;
            _registration = cancellationToken.Register(
                () => Capture(_timeProvider.GetUtcNow()));
        }

        public async Task EnsureWrittenAsync(DateTimeOffset observedAtUtc)
        {
            Capture(observedAtUtc);
            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                DateTimeOffset occurredAtUtc;
                lock (_sync)
                {
                    if (_written)
                    {
                        return;
                    }

                    occurredAtUtc = _requestedAtUtc ?? _timeProvider.GetUtcNow();
                }

                await _evidence.AppendEventAsync(
                        _runId,
                        occurredAtUtc,
                        TestRunEventKind.CancellationRequested)
                    .ConfigureAwait(false);
                lock (_sync)
                {
                    _written = true;
                }
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public void Dispose()
        {
            _registration.Dispose();
            _writeGate.Dispose();
        }

        private void Capture(DateTimeOffset requestedAtUtc)
        {
            lock (_sync)
            {
                if (_requestedAtUtc is null || requestedAtUtc < _requestedAtUtc)
                {
                    _requestedAtUtc = requestedAtUtc;
                }
            }
        }
    }
}
