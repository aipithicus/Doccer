using System;
using System.Collections.Generic;
using System.IO;

namespace Doccer.TestRunner;

internal sealed record FinalizedWorkItem(
    TestExecutionResult Result,
    CapturedProcessStream StandardOutput,
    CapturedProcessStream StandardError);

internal static class WorkItemFinalizer
{
    public static FinalizedWorkItem Finalize(
        string runId,
        TestWorkItem workItem,
        TestCaseArtifactLayout layout,
        ProcessWorkItemOutcome outcome,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var issues = new List<string>();
        TestArtifactCapture? artifactCapture = null;
        var caseRootSafe = PrepareCaseRoot(layout, issues);
        if (caseRootSafe)
        {
            try
            {
                artifactCapture = ArtifactCollectionInspector.Inspect(layout);
            }
            catch (Exception exception)
            {
                issues.Add(TestRunDiagnostics.BoundMessage(exception.Message));
            }

            try
            {
                RepositoryTree.DeleteContained(layout.CaseDirectory, layout.TempDirectory);
            }
            catch (Exception exception)
            {
                issues.Add(
                    $"Case temp cleanup failed: " +
                    TestRunDiagnostics.BoundMessage(exception.Message));
            }

            CleanUnexpectedCaseEntries(layout, issues);
        }

        var status = outcome.Status;
        var errorType = outcome.ErrorType;
        var errorMessage = outcome.ErrorMessage;
        var assertionCount = (int?)null;
        var standardOutput = outcome.StandardOutput;
        var standardError = outcome.StandardError;
        if (workItem.Kind == TestWorkItemKind.HarnessCase &&
            outcome.Status is TestExecutionStatus.Passed or TestExecutionStatus.Failed)
        {
            try
            {
                var harnessResult = ExecutableHarnessResultParser.Parse(
                    outcome.StandardOutput,
                    outcome.StandardError,
                    ExecutableHarnessAdapter.GetCaseId(workItem));
                if (harnessResult.Status != outcome.Status)
                {
                    throw new ExecutableHarnessResultValidationException(
                        $"Harness status '{harnessResult.Status}' does not match process exit " +
                        $"classification '{outcome.Status}'.");
                }

                assertionCount = harnessResult.CheckCount;
                if (status == TestExecutionStatus.Failed)
                {
                    errorType = harnessResult.ErrorType ?? "harness_failure";
                    errorMessage = harnessResult.ErrorMessage ?? errorMessage;
                }

                standardOutput = EmptyStream();
                standardError = EmptyStream();
            }
            catch (Exception exception) when (
                exception is ExecutableHarnessResultValidationException or ArgumentException)
            {
                status = TestExecutionStatus.InfrastructureError;
                errorType = "harness_protocol";
                errorMessage = TestRunDiagnostics.BoundMessage(exception.Message);
            }
        }

        if (issues.Count != 0)
        {
            if (status != TestExecutionStatus.InfrastructureError)
            {
                status = TestExecutionStatus.InfrastructureError;
                errorType = "artifact_contract";
            }
            else
            {
                errorType ??= "artifact_contract";
            }

            var issueMessage = string.Join(" ", issues);
            errorMessage = errorMessage is null
                ? issueMessage
                : $"{errorMessage} {issueMessage}";
        }

        var result = TestExecutionResult.Create(
            runId,
            layout.WorkItemId,
            status,
            outcome.StartedAtUtc,
            timeProvider.GetUtcNow(),
            outcome.ProcessExitCode,
            assertionCount: assertionCount,
            errorType: errorType,
            errorMessage: errorMessage is null
                ? null
                : TestRunDiagnostics.BoundMessage(errorMessage),
            standardOutput: standardOutput.Evidence,
            standardError: standardError.Evidence,
            artifacts: artifactCapture);
        return new FinalizedWorkItem(result, standardOutput, standardError);
    }

    private static CapturedProcessStream EmptyStream() =>
        new(Evidence: null, Content: null);

    private static bool PrepareCaseRoot(
        TestCaseArtifactLayout layout,
        ICollection<string> issues)
    {
        if (!Directory.Exists(layout.CaseDirectory) && !File.Exists(layout.CaseDirectory))
        {
            return true;
        }

        try
        {
            var attributes = File.GetAttributes(layout.CaseDirectory);
            if ((attributes & FileAttributes.Directory) != 0 &&
                (attributes & FileAttributes.ReparsePoint) == 0)
            {
                return true;
            }

            RepositoryTree.DeleteContained(
                Path.GetDirectoryName(layout.CaseDirectory)!,
                layout.CaseDirectory);
            issues.Add("The child replaced its assigned case directory with an unsafe entry.");
            return false;
        }
        catch (Exception exception)
        {
            issues.Add(
                $"The assigned case directory could not be validated: " +
                TestRunDiagnostics.BoundMessage(exception.Message));
            return false;
        }
    }

    private static void CleanUnexpectedCaseEntries(
        TestCaseArtifactLayout layout,
        ICollection<string> issues)
    {
        if (!Directory.Exists(layout.CaseDirectory))
        {
            return;
        }

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(layout.CaseDirectory))
            {
                if (RepositoryPaths.Same(entry, layout.ArtifactDirectory))
                {
                    continue;
                }

                issues.Add(
                    $"Child output outside the assigned artifact/temp directories was removed: " +
                    $"'{Path.GetFileName(entry)}'.");
                try
                {
                    RepositoryTree.DeleteContained(layout.CaseDirectory, entry);
                }
                catch (Exception exception)
                {
                    issues.Add(
                        $"Unexpected child output cleanup failed: " +
                        TestRunDiagnostics.BoundMessage(exception.Message));
                }
            }
        }
        catch (Exception exception)
        {
            issues.Add(
                $"The case workspace could not be enumerated: " +
                TestRunDiagnostics.BoundMessage(exception.Message));
        }
    }
}
