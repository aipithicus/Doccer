using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doccer.TestRunner;

internal enum TestExecutionStatus
{
    Passed,
    Failed,
    Canceled,
    TimedOut,
    NotStarted,
    InfrastructureError,
}

internal enum TestRunExitCode
{
    Success = 0,
    TestFailure = 1,
    InvalidInvocation = 2,
    Interrupted = 3,
    InfrastructureError = 4,
}

internal sealed record TestRunPlanSnapshot(
    int SchemaVersion,
    string Protocol,
    string RunId,
    DateTimeOffset RequestedAtUtc,
    string RepositoryRoot,
    string RunDirectory,
    string Configuration,
    int MaxParallel,
    IReadOnlyList<TestPlannedWorkItem> Items)
{
    public static TestRunPlanSnapshot Create(
        string runId,
        DateTimeOffset requestedAtUtc,
        string configuration,
        int maxParallel,
        IReadOnlyList<TestWorkItem> workItems,
        TestArtifactLayout artifacts)
    {
        if (string.IsNullOrWhiteSpace(runId) ||
            !string.Equals(runId, runId.Trim(), StringComparison.Ordinal) ||
            runId.Length > 64 ||
            runId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A run ID must be a trimmed, nonblank identity of at most 64 characters.",
                nameof(runId));
        }

        RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
        if (string.IsNullOrWhiteSpace(configuration))
        {
            throw new ArgumentException("A configuration is required.", nameof(configuration));
        }

        if (maxParallel < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxParallel),
                "Effective parallelism must be at least one.");
        }

        ArgumentNullException.ThrowIfNull(workItems);
        ArgumentNullException.ThrowIfNull(artifacts);
        var itemsById = workItems.ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (itemsById.Count != artifacts.Cases.Count)
        {
            throw new ArgumentException(
                "Every work item must have exactly one artifact layout.",
                nameof(artifacts));
        }

        var planned = new TestPlannedWorkItem[artifacts.Cases.Count];
        for (var index = 0; index < artifacts.Cases.Count; index++)
        {
            var itemArtifacts = artifacts.Cases[index];
            if (!itemsById.Remove(itemArtifacts.WorkItemId, out var item))
            {
                throw new ArgumentException(
                    $"Artifact layout names unknown or duplicate item '{itemArtifacts.WorkItemId}'.",
                    nameof(artifacts));
            }

            planned[index] = new TestPlannedWorkItem(item, itemArtifacts);
        }

        if (itemsById.Count != 0)
        {
            throw new ArgumentException(
                "Every work item must have exactly one artifact layout.",
                nameof(artifacts));
        }

        return new TestRunPlanSnapshot(
            TestRunnerProtocol.SchemaVersion,
            TestRunnerProtocol.RunPlan,
            runId,
            requestedAtUtc,
            artifacts.RepositoryRoot,
            artifacts.RunDirectory,
            configuration,
            maxParallel,
            Array.AsReadOnly(planned));
    }

    public string ToJson() => TestRunnerJson.Serialize(this);

    internal static void RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Machine-readable timestamps must use UTC.", parameterName);
        }
    }
}

internal sealed record TestPlannedWorkItem(
    TestWorkItem WorkItem,
    TestCaseArtifactLayout Artifacts);

internal sealed record TestExecutionResult(
    int SchemaVersion,
    string Protocol,
    string RunId,
    string WorkItemId,
    TestExecutionStatus Status,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    double? ElapsedMilliseconds,
    int? ProcessExitCode,
    int? AssertionCount,
    string? ErrorType,
    string? ErrorMessage,
    TestStreamCapture? StandardOutput,
    TestStreamCapture? StandardError,
    TestArtifactCapture? Artifacts)
{
    public static TestExecutionResult Create(
        string runId,
        string workItemId,
        TestExecutionStatus status,
        DateTimeOffset? startedAtUtc = null,
        DateTimeOffset? completedAtUtc = null,
        int? processExitCode = null,
        int? assertionCount = null,
        string? errorType = null,
        string? errorMessage = null,
        TestStreamCapture? standardOutput = null,
        TestStreamCapture? standardError = null,
        TestArtifactCapture? artifacts = null)
    {
        RequireIdentity(runId, nameof(runId));
        RequireIdentity(workItemId, nameof(workItemId));

        if (assertionCount is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assertionCount),
                "Assertion count cannot be negative.");
        }

        if (startedAtUtc.HasValue != completedAtUtc.HasValue)
        {
            throw new ArgumentException("Started and completed timestamps must be supplied together.");
        }

        double? elapsedMilliseconds = null;
        if (startedAtUtc is { } started && completedAtUtc is { } completed)
        {
            TestRunPlanSnapshot.RequireUtc(started, nameof(startedAtUtc));
            TestRunPlanSnapshot.RequireUtc(completed, nameof(completedAtUtc));
            if (completed < started)
            {
                throw new ArgumentException("Completion cannot precede start.", nameof(completedAtUtc));
            }

            elapsedMilliseconds = Math.Round((completed - started).TotalMilliseconds, 3);
        }

        switch (status)
        {
            case TestExecutionStatus.Passed when processExitCode != 0:
                throw new ArgumentException("A passed result requires process exit code zero.");
            case TestExecutionStatus.Failed when processExitCode is null or 0:
                throw new ArgumentException("A failed result requires a nonzero process exit code.");
            case TestExecutionStatus.NotStarted when
                startedAtUtc is not null ||
                processExitCode is not null ||
                assertionCount is not null ||
                standardOutput is not null ||
                standardError is not null ||
                artifacts is not null:
                throw new ArgumentException("A not-started result cannot contain execution metadata.");
        }

        if ((status is TestExecutionStatus.Passed or
             TestExecutionStatus.Failed or
             TestExecutionStatus.Canceled or
             TestExecutionStatus.TimedOut) &&
            startedAtUtc is null)
        {
            throw new ArgumentException($"Status '{status}' requires execution timestamps.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var result = new TestExecutionResult(
            TestRunnerProtocol.SchemaVersion,
            TestRunnerProtocol.Result,
            runId,
            workItemId,
            status,
            startedAtUtc,
            completedAtUtc,
            elapsedMilliseconds,
            processExitCode,
            assertionCount,
            errorType,
            errorMessage,
            standardOutput,
            standardError,
            artifacts);
        result.Validate();
        return result;
    }

    public string ToJson() => TestRunnerJson.Serialize(this);

    internal void Validate()
    {
        if (SchemaVersion != TestRunnerProtocol.SchemaVersion ||
            !string.Equals(Protocol, TestRunnerProtocol.Result, StringComparison.Ordinal))
        {
            throw new ArgumentException("The result has an unsupported evidence contract.");
        }

        RequireIdentity(RunId, nameof(RunId));
        RequireIdentity(WorkItemId, nameof(WorkItemId));
        if (!Enum.IsDefined(Status))
        {
            throw new ArgumentOutOfRangeException(nameof(Status));
        }

        if (AssertionCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AssertionCount));
        }

        StandardOutput?.Validate();
        StandardError?.Validate();
        Artifacts?.Validate();

        if (StartedAtUtc.HasValue != CompletedAtUtc.HasValue)
        {
            throw new ArgumentException("Started and completed timestamps must be supplied together.");
        }

        if (StartedAtUtc is { } started && CompletedAtUtc is { } completed)
        {
            TestRunPlanSnapshot.RequireUtc(started, nameof(StartedAtUtc));
            TestRunPlanSnapshot.RequireUtc(completed, nameof(CompletedAtUtc));
            if (completed < started)
            {
                throw new ArgumentException("Completion cannot precede start.", nameof(CompletedAtUtc));
            }

            var expectedElapsed = Math.Round((completed - started).TotalMilliseconds, 3);
            if (ElapsedMilliseconds != expectedElapsed)
            {
                throw new ArgumentException("Elapsed duration must match the result timestamps.");
            }
        }
        else if (ElapsedMilliseconds is not null)
        {
            throw new ArgumentException("Elapsed duration requires execution timestamps.");
        }

        switch (Status)
        {
            case TestExecutionStatus.Passed when ProcessExitCode != 0:
                throw new ArgumentException("A passed result requires process exit code zero.");
            case TestExecutionStatus.Failed when ProcessExitCode is null or 0:
                throw new ArgumentException("A failed result requires a nonzero process exit code.");
            case TestExecutionStatus.NotStarted when
                StartedAtUtc is not null ||
                ProcessExitCode is not null ||
                AssertionCount is not null ||
                StandardOutput is not null ||
                StandardError is not null ||
                Artifacts is not null:
                throw new ArgumentException("A not-started result cannot contain execution metadata.");
        }

        if ((Status is TestExecutionStatus.Passed or
             TestExecutionStatus.Failed or
             TestExecutionStatus.Canceled or
             TestExecutionStatus.TimedOut) &&
            StartedAtUtc is null)
        {
            throw new ArgumentException($"Status '{Status}' requires execution timestamps.");
        }
    }

    private static void RequireIdentity(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException("A stable nonblank identity is required.", parameterName);
        }
    }
}

internal sealed record TestStatusCounts(
    int Passed,
    int Failed,
    int Canceled,
    int TimedOut,
    int NotStarted,
    int InfrastructureError);

internal sealed record TestRunDetailReference(
    string WorkItemId,
    string ResultPath);

internal sealed record TestRunSummary(
    int SchemaVersion,
    string Protocol,
    string RunId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int ExitCode,
    TestStatusCounts Counts,
    IReadOnlyList<TestExecutionResult> Results,
    IReadOnlyList<TestRunDetailReference> Details)
{
    public static TestRunSummary Create(
        TestRunPlanSnapshot plan,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        IReadOnlyList<TestExecutionResult> results)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(results);
        TestRunPlanSnapshot.RequireUtc(startedAtUtc, nameof(startedAtUtc));
        TestRunPlanSnapshot.RequireUtc(completedAtUtc, nameof(completedAtUtc));
        if (startedAtUtc < plan.RequestedAtUtc || completedAtUtc < startedAtUtc)
        {
            throw new ArgumentException("Run timestamps must be monotonic.");
        }

        var resultsById = new Dictionary<string, TestExecutionResult>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            result.Validate();

            if (!string.Equals(result.RunId, plan.RunId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Result '{result.WorkItemId}' belongs to a different run.",
                    nameof(results));
            }

            if (result.StartedAtUtc is { } resultStarted && resultStarted < startedAtUtc ||
                result.CompletedAtUtc is { } resultCompleted && resultCompleted > completedAtUtc)
            {
                throw new ArgumentException(
                    $"Result '{result.WorkItemId}' lies outside the enclosing run interval.",
                    nameof(results));
            }

            if (!resultsById.TryAdd(result.WorkItemId, result))
            {
                throw new ArgumentException(
                    $"Duplicate result for '{result.WorkItemId}'.",
                    nameof(results));
            }
        }

        var ordered = new TestExecutionResult[plan.Items.Count];
        for (var index = 0; index < plan.Items.Count; index++)
        {
            var id = plan.Items[index].WorkItem.Id;
            if (!resultsById.Remove(id, out var result))
            {
                throw new ArgumentException($"Missing terminal result for '{id}'.", nameof(results));
            }

            ordered[index] = result;
        }

        if (resultsById.Count != 0)
        {
            throw new ArgumentException(
                $"Result names an unplanned item '{resultsById.Keys.OrderBy(id => id, StringComparer.Ordinal).First()}'.",
                nameof(results));
        }

        var counts = new TestStatusCounts(
            ordered.Count(result => result.Status == TestExecutionStatus.Passed),
            ordered.Count(result => result.Status == TestExecutionStatus.Failed),
            ordered.Count(result => result.Status == TestExecutionStatus.Canceled),
            ordered.Count(result => result.Status == TestExecutionStatus.TimedOut),
            ordered.Count(result => result.Status == TestExecutionStatus.NotStarted),
            ordered.Count(result => result.Status == TestExecutionStatus.InfrastructureError));
        var exitCode = ResolveExitCode(ordered);
        var details = new List<TestRunDetailReference>();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (!TestEvidenceMaterializationPolicy.RequiresCaseDetails(ordered[index]))
            {
                continue;
            }

            var resultPath = Path.GetRelativePath(
                    plan.RepositoryRoot,
                    plan.Items[index].Artifacts.ResultPath)
                .Replace('\\', '/');
            details.Add(new TestRunDetailReference(ordered[index].WorkItemId, resultPath));
        }

        return new TestRunSummary(
            TestRunnerProtocol.SchemaVersion,
            TestRunnerProtocol.Summary,
            plan.RunId,
            plan.RequestedAtUtc,
            startedAtUtc,
            completedAtUtc,
            (int)exitCode,
            counts,
            Array.AsReadOnly(ordered),
            details.AsReadOnly());
    }

    public string ToJson() => TestRunnerJson.Serialize(this);

    public static TestRunExitCode ResolveExitCode(IEnumerable<TestExecutionResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var exitCode = TestRunExitCode.Success;
        foreach (var result in results)
        {
            result.Validate();
            exitCode = result.Status switch
            {
                TestExecutionStatus.InfrastructureError => TestRunExitCode.InfrastructureError,
                TestExecutionStatus.Canceled or
                TestExecutionStatus.TimedOut or
                TestExecutionStatus.NotStarted =>
                    exitCode < TestRunExitCode.Interrupted
                        ? TestRunExitCode.Interrupted
                        : exitCode,
                TestExecutionStatus.Failed =>
                    exitCode < TestRunExitCode.TestFailure
                        ? TestRunExitCode.TestFailure
                        : exitCode,
                TestExecutionStatus.Passed => exitCode,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(results),
                    $"Unknown execution status '{result.Status}'."),
            };

        }

        return exitCode;
    }
}
