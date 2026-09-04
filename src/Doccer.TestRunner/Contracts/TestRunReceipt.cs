using System;
using System.IO;

namespace Doccer.TestRunner;

internal enum TestRunOutcome
{
    Passed,
    Failed,
    Interrupted,
    InfrastructureError,
}

internal sealed record TestRunReceipt(
    int SchemaVersion,
    string Protocol,
    string RunId,
    TestRunOutcome Outcome,
    int ExitCode,
    TestStatusCounts Counts,
    double ElapsedMilliseconds,
    string SummaryPath,
    int PrunedRunCount)
{
    public const int MaximumConsoleLineLength = 768;

    public static TestRunReceipt Create(
        TestRunSummary summary,
        TestArtifactLayout artifacts,
        int prunedRunCount = 0)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(artifacts);
        if (prunedRunCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(prunedRunCount));
        }
        if (summary.SchemaVersion != TestRunnerProtocol.SchemaVersion ||
            !string.Equals(summary.Protocol, TestRunnerProtocol.Summary, StringComparison.Ordinal))
        {
            throw new ArgumentException("The summary has an unsupported evidence contract.", nameof(summary));
        }

        TestRunPlanSnapshot.RequireUtc(summary.StartedAtUtc, nameof(summary));
        TestRunPlanSnapshot.RequireUtc(summary.CompletedAtUtc, nameof(summary));
        if (summary.CompletedAtUtc < summary.StartedAtUtc)
        {
            throw new ArgumentException("Summary timestamps must be monotonic.", nameof(summary));
        }

        if (!RepositoryPaths.Contains(artifacts.RepositoryRoot, artifacts.SummaryPath, allowRoot: false))
        {
            throw new ArgumentException("The summary path must remain beneath the repository.", nameof(artifacts));
        }

        var elapsedMilliseconds = Math.Round(
            (summary.CompletedAtUtc - summary.StartedAtUtc).TotalMilliseconds,
            3);
        var summaryPath = Path.GetRelativePath(
                artifacts.RepositoryRoot,
                artifacts.SummaryPath)
            .Replace('\\', '/');

        return new TestRunReceipt(
            TestRunnerProtocol.SchemaVersion,
            TestRunnerProtocol.Receipt,
            summary.RunId,
            OutcomeFor(summary.ExitCode),
            summary.ExitCode,
            summary.Counts,
            elapsedMilliseconds,
            summaryPath,
            prunedRunCount);
    }

    public string ToJsonLine()
    {
        var line = TestRunnerJson.SerializeCompact(this);
        if (line.Length > MaximumConsoleLineLength)
        {
            throw new InvalidOperationException(
                $"A console receipt cannot exceed {MaximumConsoleLineLength} characters.");
        }

        return line;
    }

    private static TestRunOutcome OutcomeFor(int exitCode) =>
        (TestRunExitCode)exitCode switch
        {
            TestRunExitCode.Success => TestRunOutcome.Passed,
            TestRunExitCode.TestFailure => TestRunOutcome.Failed,
            TestRunExitCode.Interrupted => TestRunOutcome.Interrupted,
            TestRunExitCode.InfrastructureError => TestRunOutcome.InfrastructureError,
            _ => throw new ArgumentOutOfRangeException(
                nameof(exitCode),
                $"Unknown run exit code '{exitCode}'."),
        };
}
