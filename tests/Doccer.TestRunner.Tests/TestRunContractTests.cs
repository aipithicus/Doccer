using System;
using System.Collections.Generic;

namespace Doccer.TestRunner.Tests;

internal static partial class Program
{
    private static readonly DateTimeOffset ContractRequestedAt =
        new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static void RunContractsAccountForEveryPlannedItem()
    {
        var (plan, items) = CreateRunPlan();
        var started = ContractRequestedAt.AddSeconds(1);
        var completed = ContractRequestedAt.AddSeconds(5);
        var passed = TestExecutionResult.Create(
            plan.RunId,
            items[0].Id,
            TestExecutionStatus.Passed,
            started,
            started.AddMilliseconds(25),
            processExitCode: 0,
            assertionCount: 3);
        var failed = TestExecutionResult.Create(
            plan.RunId,
            items[1].Id,
            TestExecutionStatus.Failed,
            started,
            started.AddMilliseconds(40),
            processExitCode: 7,
            assertionCount: 1,
            errorType: "fixture_failure",
            errorMessage: "expected failure");

        var summary = TestRunSummary.Create(
            plan,
            started,
            completed,
            new[] { failed, passed });

        Equal(1, summary.ExitCode, "ordinary failure exit code");
        Equal(1, summary.Counts.Passed, "summary passed count");
        Equal(1, summary.Counts.Failed, "summary failed count");
        Equal(items[0].Id, summary.Results[0].WorkItemId, "summary follows frozen plan order");
        Equal(items[1].Id, summary.Results[1].WorkItemId, "summary second plan item");
        Equal(1, summary.Details.Count, "summary selective detail count");
        Equal(items[1].Id, summary.Details[0].WorkItemId, "summary failed detail identity");
        True(
            summary.Details[0].ResultPath.StartsWith(
                "build/test-runs/run-contract/c/0002-",
                StringComparison.Ordinal),
            "summary detail path is direct and repository-relative");
        True(
            summary.Details[0].ResultPath.EndsWith("/result.json", StringComparison.Ordinal),
            "summary detail result path");
        Equal(25d, passed.ElapsedMilliseconds, "result duration");
        Contains("\"protocol\": \"doccer-test-run-plan\"", plan.ToJson(), "run-plan protocol JSON");
        Contains("\"status\": \"passed\"", passed.ToJson(), "result status JSON");
        Contains("\"protocol\": \"doccer-test-summary\"", summary.ToJson(), "summary protocol JSON");
        Contains("\"resultPath\": \"build/test-runs/run-contract/c/0002-", summary.ToJson(), "summary detail link JSON");

        Throws<ArgumentException>(
            () => TestRunSummary.Create(plan, started, completed, new[] { passed }),
            "missing result rejection");
        Throws<ArgumentException>(
            () => TestRunSummary.Create(plan, started, completed, new[] { passed, passed }),
            "duplicate result rejection");
        var unplanned = passed with { WorkItemId = "not.planned" };
        Throws<ArgumentException>(
            () => TestRunSummary.Create(plan, started, completed, new[] { passed, failed, unplanned }),
            "unplanned result rejection");
        Throws<ArgumentException>(
            () => TestRunSummary.Create(
                plan,
                started,
                completed,
                new[] { passed with { Protocol = "other-result" }, failed }),
            "result protocol rejection");
        Throws<ArgumentException>(
            () => TestRunSummary.Create(
                plan,
                started,
                completed,
                new[] { passed with { ProcessExitCode = 9 }, failed }),
            "mutated result shape rejection");
        var tooEarly = TestExecutionResult.Create(
            plan.RunId,
            items[0].Id,
            TestExecutionStatus.Passed,
            ContractRequestedAt,
            ContractRequestedAt.AddMilliseconds(1),
            processExitCode: 0);
        Throws<ArgumentException>(
            () => TestRunSummary.Create(plan, started, completed, new[] { tooEarly, failed }),
            "result outside run interval rejection");
        Throws<ArgumentException>(
            () => TestRunPlanSnapshot.Create(
                new string('r', 65),
                ContractRequestedAt,
                "Debug",
                maxParallel: 2,
                items,
                TestArtifactLayout.Create(
                    ContractRepositoryRoot,
                    "build/test-runs/long-run-id",
                    items)),
            "run identity length rejection");
    }

    private static void ResultStatusAndExitPrecedenceAreExact()
    {
        var (plan, items) = CreateRunPlan();
        var started = ContractRequestedAt.AddSeconds(1);
        var completed = started.AddSeconds(1);
        var passed = TestExecutionResult.Create(
            plan.RunId,
            items[0].Id,
            TestExecutionStatus.Passed,
            started,
            completed,
            processExitCode: 0);
        var failed = TestExecutionResult.Create(
            plan.RunId,
            items[1].Id,
            TestExecutionStatus.Failed,
            started,
            completed,
            processExitCode: 1);
        var timedOut = TestExecutionResult.Create(
            plan.RunId,
            items[0].Id,
            TestExecutionStatus.TimedOut,
            started,
            completed);
        var notStarted = TestExecutionResult.Create(
            plan.RunId,
            items[1].Id,
            TestExecutionStatus.NotStarted);
        var infrastructure = TestExecutionResult.Create(
            plan.RunId,
            items[0].Id,
            TestExecutionStatus.InfrastructureError,
            errorType: "launch_error",
            errorMessage: "did not start");

        Equal(
            TestRunExitCode.Success,
            TestRunSummary.ResolveExitCode(new[] { passed }),
            "success exit precedence");
        Equal(
            TestRunExitCode.TestFailure,
            TestRunSummary.ResolveExitCode(new[] { passed, failed }),
            "failure exit precedence");
        Equal(
            TestRunExitCode.Interrupted,
            TestRunSummary.ResolveExitCode(new[] { failed, timedOut, notStarted }),
            "interrupted outranks failure");
        Equal(
            TestRunExitCode.InfrastructureError,
            TestRunSummary.ResolveExitCode(new[] { timedOut, infrastructure, failed }),
            "infrastructure outranks interruption");

        Throws<ArgumentException>(
            () => TestExecutionResult.Create(
                plan.RunId,
                items[0].Id,
                TestExecutionStatus.Passed,
                started,
                completed,
                processExitCode: 5),
            "passed result nonzero exit rejection");
        Throws<ArgumentException>(
            () => TestExecutionResult.Create(
                plan.RunId,
                items[0].Id,
                TestExecutionStatus.Failed,
                started,
                completed,
                processExitCode: 0),
            "failed result zero exit rejection");
        Throws<ArgumentException>(
            () => TestExecutionResult.Create(
                plan.RunId,
                items[0].Id,
                TestExecutionStatus.NotStarted,
                started,
                completed),
            "not-started execution metadata rejection");
        Throws<ArgumentException>(
            () => TestExecutionResult.Create(
                plan.RunId,
                items[0].Id,
                TestExecutionStatus.Passed,
                started,
                completed,
                processExitCode: 0,
                assertionCount: -1),
            "negative assertion count rejection");
        Throws<ArgumentOutOfRangeException>(
            () => TestRunSummary.ResolveExitCode(
                new[] { passed with { Status = (TestExecutionStatus)999 } }),
            "unknown result status rejection");
    }

    private static void ReceiptAndDetailMaterializationAreBounded()
    {
        var (plan, items) = CreateRunPlan();
        var artifacts = TestArtifactLayout.Create(
            ContractRepositoryRoot,
            plan.RunDirectory,
            items);
        var started = ContractRequestedAt.AddSeconds(1);
        var completed = started.AddSeconds(1);
        var passed = TestExecutionResult.Create(
            plan.RunId,
            items[0].Id,
            TestExecutionStatus.Passed,
            started,
            completed,
            processExitCode: 0,
            assertionCount: 3);
        var failed = TestExecutionResult.Create(
            plan.RunId,
            items[1].Id,
            TestExecutionStatus.Failed,
            started,
            completed,
            processExitCode: 1,
            assertionCount: 1,
            errorType: "fixture_failure",
            errorMessage: "expected failure");
        var chattyPass = TestExecutionResult.Create(
            plan.RunId,
            items[0].Id,
            TestExecutionStatus.Passed,
            started,
            completed,
            processExitCode: 0,
            assertionCount: 3,
            standardOutput: TestStreamCapture.Create(300_000));
        var summary = TestRunSummary.Create(
            plan,
            started,
            completed,
            new[] { passed, failed });

        var receipt = TestRunReceipt.Create(summary, artifacts);
        var receiptLine = receipt.ToJsonLine();
        Equal("doccer-test-receipt", receipt.Protocol, "receipt protocol");
        Equal(TestRunOutcome.Failed, receipt.Outcome, "receipt outcome");
        Equal(1000d, receipt.ElapsedMilliseconds, "receipt elapsed duration");
        Equal(
            "build/test-runs/run-contract/summary.json",
            receipt.SummaryPath,
            "receipt repository-relative summary path");
        Equal(0, receipt.PrunedRunCount, "receipt default prune count");
        Equal(16, TestRunRetentionContract.MaximumFinalizedRunCount, "finalized run retention bound");
        True(!receiptLine.Contains('\n'), "receipt is one console line");
        Contains("\"prunedRunCount\":0", receiptLine, "receipt prune residue JSON");
        True(
            receiptLine.Length <= TestRunReceipt.MaximumConsoleLineLength,
            "receipt console length bound");
        True(
            !receiptLine.Contains(ContractRepositoryRoot, StringComparison.OrdinalIgnoreCase),
            "receipt omits absolute repository path");

        True(
            !TestEvidenceMaterializationPolicy.RequiresCaseDetails(passed),
            "clean pass remains summary-only");
        True(
            TestEvidenceMaterializationPolicy.RequiresCaseDetails(chattyPass),
            "nonempty stdout retains details");
        var chattySummary = TestRunSummary.Create(
            plan,
            started,
            completed,
            new[] { chattyPass, failed });
        Equal(2, chattySummary.Details.Count, "summary links chatty pass and failure details");
        Equal(300_000L, chattyPass.StandardOutput?.ObservedBytes, "stream observed byte count");
        Equal(
            TestCaptureContract.MaximumRetainedBytesPerStream,
            chattyPass.StandardOutput?.RetainedBytes,
            "stream retained byte cap");
        Equal(true, chattyPass.StandardOutput?.Truncated, "stream truncation residue");
        var stderrPass = passed with { StandardError = TestStreamCapture.Create(1) };
        True(
            TestEvidenceMaterializationPolicy.RequiresCaseDetails(stderrPass),
            "nonempty stderr retains details");
        var artifactPass = passed with
        {
            Artifacts = TestArtifactCapture.Create(2, totalBytes: 20, largestFileBytes: 12),
        };
        True(
            TestEvidenceMaterializationPolicy.RequiresCaseDetails(artifactPass),
            "child artifact retains details");
        var emptyArtifact = TestArtifactCapture.Create(1, totalBytes: 0, largestFileBytes: 0);
        Equal(0L, emptyArtifact.TotalBytes, "empty artifact remains evidence");
        True(
            TestEvidenceMaterializationPolicy.RequiresCaseDetails(failed),
            "failure retains details");
        Throws<ArgumentOutOfRangeException>(
            () => TestStreamCapture.Create(0),
            "empty stream capture rejection");
        Throws<ArgumentException>(
            () => TestEvidenceMaterializationPolicy.RequiresCaseDetails(
                passed with { StandardOutput = new TestStreamCapture(2, 1, false) }),
            "mutated stream capture rejection");
        Throws<ArgumentOutOfRangeException>(
            () => TestArtifactCapture.Create(
                TestCaptureContract.MaximumArtifactFileCount + 1,
                totalBytes: 100,
                largestFileBytes: 1),
            "artifact file count cap");
        Throws<ArgumentOutOfRangeException>(
            () => TestArtifactCapture.Create(
                fileCount: 1,
                totalBytes: TestCaptureContract.MaximumArtifactTotalBytes + 1,
                largestFileBytes: 1),
            "artifact total byte cap");
        Throws<ArgumentOutOfRangeException>(
            () => TestArtifactCapture.Create(
                fileCount: 1,
                totalBytes: TestCaptureContract.MaximumArtifactFileBytes + 1,
                largestFileBytes: TestCaptureContract.MaximumArtifactFileBytes + 1),
            "artifact single-file byte cap");
        Throws<ArgumentOutOfRangeException>(
            () => TestRunReceipt.Create(summary with { ExitCode = 99 }, artifacts),
            "unknown receipt outcome rejection");
        Throws<ArgumentOutOfRangeException>(
            () => TestRunReceipt.Create(summary, artifacts, prunedRunCount: -1),
            "negative receipt prune count rejection");
        Throws<ArgumentException>(
            () => TestRunReceipt.Create(
                summary with { CompletedAtUtc = summary.StartedAtUtc.AddTicks(-1) },
                artifacts),
            "receipt monotonic timestamp rejection");
    }

    private static (TestRunPlanSnapshot Plan, IReadOnlyList<TestWorkItem> Items) CreateRunPlan()
    {
        var items = CommandPlanExpander.Expand(
            TestPlanParser.Parse(ValidPlanJson, ContractRepositoryRoot)).Commands;
        var artifacts = TestArtifactLayout.Create(
            ContractRepositoryRoot,
            "build/test-runs/run-contract",
            items);
        var plan = TestRunPlanSnapshot.Create(
            "run-contract",
            ContractRequestedAt,
            "Debug",
            maxParallel: 2,
            items,
            artifacts);
        return (plan, items);
    }
}
