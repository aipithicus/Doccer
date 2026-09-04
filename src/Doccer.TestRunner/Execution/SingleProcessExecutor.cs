using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Doccer.TestRunner;

internal sealed record ProcessWorkItemOutcome(
    TestExecutionStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int? ProcessExitCode,
    string? ErrorType,
    string? ErrorMessage,
    CapturedProcessStream StandardOutput,
    CapturedProcessStream StandardError,
    DateTimeOffset? CancellationRequestedAtUtc);

internal static class SingleProcessExecutor
{
    private static readonly TimeSpan TerminationGrace = TimeSpan.FromSeconds(10);

    public static async Task<ProcessWorkItemOutcome> ExecuteAsync(
        string repositoryRoot,
        string runId,
        TestWorkItem workItem,
        TestArtifactLayout runLayout,
        TestCaseArtifactLayout caseLayout,
        DateTimeOffset startedAtUtc,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(runLayout);
        ArgumentNullException.ThrowIfNull(caseLayout);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var empty = new CapturedProcessStream(Evidence: null, Content: null);
        ProcessStartInfo startInfo;
        try
        {
            startInfo = CreateStartInfo(
                repositoryRoot,
                runId,
                workItem,
                runLayout,
                caseLayout);
            RepositoryEntryGuard.CreateDirectoryPath(repositoryRoot, caseLayout.TempDirectory);
        }
        catch (Exception exception)
        {
            return InfrastructureFailure(
                startedAtUtc,
                timeProvider.GetUtcNow(),
                empty,
                empty,
                exception,
                cancellationRequestedAtUtc: null);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The child process did not start.");
            }
        }
        catch (Exception exception)
        {
            return InfrastructureFailure(
                startedAtUtc,
                timeProvider.GetUtcNow(),
                empty,
                empty,
                exception,
                cancellationRequestedAtUtc: null);
        }

        var standardOutputTask = BoundedProcessStreamCapture.CaptureAsync(
            process.StandardOutput.BaseStream);
        var standardErrorTask = BoundedProcessStreamCapture.CaptureAsync(
            process.StandardError.BaseStream);
        var exitTask = process.WaitForExitAsync();
        var cancellationSignal = new TaskCompletionSource<DateTimeOffset>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            () => cancellationSignal.TrySetResult(timeProvider.GetUtcNow()));
        var timeoutTask = workItem.TimeoutMilliseconds is { } timeoutMilliseconds
            ? Task.Delay(timeoutMilliseconds)
            : Task.Delay(Timeout.Infinite, CancellationToken.None);

        TestExecutionStatus status;
        string? errorType = null;
        string? errorMessage = null;
        int? processExitCode = null;
        DateTimeOffset? cancellationRequestedAtUtc = null;
        Exception? terminationFailure = null;
        var completedTask = await Task.WhenAny(
                exitTask,
                timeoutTask,
                cancellationSignal.Task)
            .ConfigureAwait(false);
        if (exitTask.IsCompleted)
        {
            try
            {
                await exitTask.ConfigureAwait(false);
                processExitCode = process.ExitCode;
                status = processExitCode == 0
                    ? TestExecutionStatus.Passed
                    : TestExecutionStatus.Failed;
                if (status == TestExecutionStatus.Failed)
                {
                    errorType = "process_exit";
                    errorMessage = $"Child process exited with code {processExitCode}.";
                }
            }
            catch (Exception exception)
            {
                status = TestExecutionStatus.InfrastructureError;
                errorType = "process_infrastructure";
                errorMessage = TestRunDiagnostics.BoundMessage(exception.Message);
                terminationFailure = await TerminateAsync(process, exitTask).ConfigureAwait(false)
                    ?? exception;
            }
        }
        else
        {
            if (ReferenceEquals(completedTask, cancellationSignal.Task))
            {
                cancellationRequestedAtUtc = await cancellationSignal.Task.ConfigureAwait(false);
                status = TestExecutionStatus.Canceled;
                errorType = "canceled";
                errorMessage = "Cancellation was requested while the child process was running.";
            }
            else
            {
                status = TestExecutionStatus.TimedOut;
                errorType = "timeout";
                errorMessage =
                    $"Child process exceeded its {workItem.TimeoutMilliseconds}-millisecond timeout.";
            }

            terminationFailure = await TerminateAsync(process, exitTask).ConfigureAwait(false);
        }

        CapturedProcessStream standardOutput;
        CapturedProcessStream standardError;
        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            standardOutput = await standardOutputTask.ConfigureAwait(false);
            standardError = await standardErrorTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            standardOutput = standardOutputTask.IsCompletedSuccessfully
                ? standardOutputTask.Result
                : empty;
            standardError = standardErrorTask.IsCompletedSuccessfully
                ? standardErrorTask.Result
                : empty;
            return InfrastructureFailure(
                startedAtUtc,
                timeProvider.GetUtcNow(),
                standardOutput,
                standardError,
                exception,
                cancellationRequestedAtUtc);
        }

        if (terminationFailure is not null)
        {
            return InfrastructureFailure(
                startedAtUtc,
                timeProvider.GetUtcNow(),
                standardOutput,
                standardError,
                terminationFailure,
                cancellationRequestedAtUtc);
        }

        return new ProcessWorkItemOutcome(
            status,
            startedAtUtc,
            timeProvider.GetUtcNow(),
            processExitCode,
            errorType,
            errorMessage,
            standardOutput,
            standardError,
            cancellationRequestedAtUtc);
    }

    private static ProcessStartInfo CreateStartInfo(
        string repositoryRoot,
        string runId,
        TestWorkItem workItem,
        TestArtifactLayout runLayout,
        TestCaseArtifactLayout caseLayout)
    {
        var root = RepositoryPaths.NormalizeRoot(repositoryRoot, nameof(repositoryRoot));
        var workingDirectory = RepositoryPaths.ResolveContained(
            root,
            workItem.WorkingDirectory,
            nameof(workItem.WorkingDirectory),
            allowRoot: true);
        RepositoryEntryGuard.RequireExistingWithoutReparsePoints(
            root,
            workingDirectory,
            directory: true,
            nameof(workItem.WorkingDirectory));

        var executable = workItem.Executable;
        if (executable.Contains('/') || executable.Contains('\\'))
        {
            executable = RepositoryPaths.ResolveContained(
                root,
                executable,
                nameof(workItem.Executable),
                allowRoot: false);
            RepositoryEntryGuard.RequireExistingWithoutReparsePoints(
                root,
                executable,
                directory: false,
                nameof(workItem.Executable));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in workItem.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in ChildEnvironmentContract.CreateAdditions(
                     workItem.Environment,
                     runId,
                     runLayout,
                     caseLayout))
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return startInfo;
    }

    private static async Task<Exception?> TerminateAsync(Process process, Task exitTask)
    {
        Exception? killFailure = null;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
        catch (Exception exception)
        {
            killFailure = exception;
        }

        try
        {
            await exitTask.WaitAsync(TerminationGrace).ConfigureAwait(false);
            return process.HasExited ? null : killFailure;
        }
        catch (Exception exception)
        {
            try
            {
                process.StandardOutput.Close();
                process.StandardError.Close();
            }
            catch (Exception)
            {
            }

            return killFailure ?? new TimeoutException(
                $"The child process did not terminate within {TerminationGrace.TotalSeconds:0} seconds.",
                exception);
        }
    }

    private static ProcessWorkItemOutcome InfrastructureFailure(
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        CapturedProcessStream standardOutput,
        CapturedProcessStream standardError,
        Exception exception,
        DateTimeOffset? cancellationRequestedAtUtc)
    {
        return new ProcessWorkItemOutcome(
            TestExecutionStatus.InfrastructureError,
            startedAtUtc,
            completedAtUtc,
            ProcessExitCode: null,
            ErrorType: "process_infrastructure",
            ErrorMessage: TestRunDiagnostics.BoundMessage(exception.Message),
            standardOutput,
            standardError,
            cancellationRequestedAtUtc);
    }
}
