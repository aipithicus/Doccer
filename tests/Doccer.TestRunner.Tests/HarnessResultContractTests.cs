using System;
using System.Collections.Generic;
using System.IO;

namespace Doccer.TestRunner.Tests;

internal static partial class Program
{
    private static void ExecutableHarnessResultsAreStrictAndAssimilated()
    {
        var passedJson = CreateHarnessResultJson("selected.case", "passed", checkCount: 7);
        var parsed = ExecutableHarnessResultParser.Parse(
            CaptureToolText(passedJson),
            CaptureToolText(string.Empty),
            "selected.case");
        Equal("doccer.contracts", parsed.SuiteId, "harness result suite");
        Equal("selected.case", parsed.CaseId, "harness result case");
        Equal(TestExecutionStatus.Passed, parsed.Status, "harness result status");
        Equal(7, parsed.CheckCount, "harness result assertion count");

        RejectsHarnessResult(
            passedJson.Replace("selected.case", "foreign.case", StringComparison.Ordinal),
            "selected.case",
            "instead of",
            "foreign harness result case rejection");
        RejectsHarnessResult(
            passedJson.Replace("\"passed\"", "\"Passed\"", StringComparison.Ordinal),
            "selected.case",
            "status must be",
            "noncanonical harness result status rejection");
        RejectsHarnessResult(
            passedJson.Replace(
                "\"checkCount\":7",
                "\"checkCount\":7,\"mystery\":true",
                StringComparison.Ordinal),
            "selected.case",
            "could not be mapped",
            "unknown harness result property rejection");
        RejectsHarnessResult(
            passedJson.Replace(
                "\"caseId\":\"selected.case\"",
                "\"caseId\":\"selected.case\",\"caseId\":\"duplicate\"",
                StringComparison.Ordinal),
            "selected.case",
            "Duplicate JSON property",
            "duplicate harness result property rejection");
        Throws<ExecutableHarnessResultValidationException>(
            () => ExecutableHarnessResultParser.Parse(
                CaptureToolText(passedJson),
                CaptureToolText("unexpected stderr"),
                "selected.case"),
            "harness result stderr rejection");

        var testRoot = CreateContainedTestRoot("harness-result");
        try
        {
            var workItem = CreateHarnessResultWorkItem();
            var passed = FinalizeHarnessOutcome(
                testRoot,
                "passed",
                workItem,
                TestExecutionStatus.Passed,
                processExitCode: 0,
                passedJson);
            Equal(TestExecutionStatus.Passed, passed.Result.Status, "assimilated harness pass");
            Equal(7, passed.Result.AssertionCount, "assimilated assertion count");
            Equal<TestStreamCapture?>(
                null,
                passed.Result.StandardOutput,
                "assimilated stdout omitted from result");
            Equal<byte[]?>(null, passed.StandardOutput.Content, "assimilated stdout bytes discarded");
            True(
                !TestEvidenceMaterializationPolicy.RequiresCaseDetails(passed.Result),
                "clean harness pass remains summary-only");

            var failedJson = CreateHarnessResultJson(
                "selected.case",
                "failed",
                checkCount: 3,
                errorType: "FixtureFailure",
                errorMessage: "expected failure");
            var failed = FinalizeHarnessOutcome(
                testRoot,
                "failed",
                workItem,
                TestExecutionStatus.Failed,
                processExitCode: 1,
                failedJson);
            Equal(TestExecutionStatus.Failed, failed.Result.Status, "assimilated harness failure");
            Equal(3, failed.Result.AssertionCount, "failed harness assertion count");
            Equal("FixtureFailure", failed.Result.ErrorType, "failed harness error type");
            Equal("expected failure", failed.Result.ErrorMessage, "failed harness error message");
            Equal<TestStreamCapture?>(
                null,
                failed.Result.StandardOutput,
                "failed harness protocol stdout consumed");
            True(
                TestEvidenceMaterializationPolicy.RequiresCaseDetails(failed.Result),
                "failed harness retains structured detail");

            var malformed = FinalizeHarnessOutcome(
                testRoot,
                "malformed",
                workItem,
                TestExecutionStatus.Passed,
                processExitCode: 0,
                "not json");
            Equal(
                TestExecutionStatus.InfrastructureError,
                malformed.Result.Status,
                "malformed harness result is infrastructure failure");
            Equal("harness_protocol", malformed.Result.ErrorType, "malformed harness error type");
            True(malformed.Result.StandardOutput is not null, "malformed harness stdout retained");
            True(malformed.StandardOutput.Content is not null, "malformed harness bytes retained");
        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, testRoot);
        }
    }

    private static FinalizedWorkItem FinalizeHarnessOutcome(
        string repositoryRoot,
        string runName,
        TestWorkItem workItem,
        TestExecutionStatus status,
        int processExitCode,
        string standardOutput)
    {
        var artifacts = TestArtifactLayout.Create(
            repositoryRoot,
            $"build/test-runs/{runName}",
            new[] { workItem });
        var caseLayout = artifacts.Cases[0];
        Directory.CreateDirectory(caseLayout.TempDirectory);
        var startedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        var outcome = new ProcessWorkItemOutcome(
            status,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            processExitCode,
            status == TestExecutionStatus.Failed ? "process_exit" : null,
            status == TestExecutionStatus.Failed ? "Child process failed." : null,
            CaptureToolText(standardOutput),
            CaptureToolText(string.Empty),
            CancellationRequestedAtUtc: null);
        var finalized = WorkItemFinalizer.Finalize(
            "harness-result-run",
            workItem,
            caseLayout,
            outcome,
            TimeProvider.System);
        True(!Directory.Exists(caseLayout.TempDirectory), $"{runName} harness temp removed");
        return finalized;
    }

    private static TestWorkItem CreateHarnessResultWorkItem()
    {
        return new TestWorkItem(
            "doccer.contracts/selected.case",
            "Selected case",
            TestWorkItemKind.HarnessCase,
            "doccer.contracts",
            "tests/Doccer.Tests/Doccer.Tests.csproj",
            "dotnet",
            Array.AsReadOnly(
                new[]
                {
                    "exec",
                    Path.Combine(AppContext.BaseDirectory, "Doccer.Tests.dll"),
                    "run",
                    "--case",
                    "selected.case",
                    "--format",
                    "json",
                }),
            ".",
            new Dictionary<string, string>(),
            TimeoutMilliseconds: 5000,
            Group: "contracts",
            TestConcurrency.Parallel);
    }

    private static string CreateHarnessResultJson(
        string caseId,
        string status,
        int checkCount,
        string? errorType = null,
        string? errorMessage = null)
    {
        return TestRunnerJson.SerializeCompact(
            new
            {
                schemaVersion = ExecutableHarnessCatalogParser.SchemaVersion,
                protocol = ExecutableHarnessCatalogParser.Protocol,
                suiteId = "doccer.contracts",
                caseId,
                status,
                checkCount,
                elapsedMilliseconds = 12.5,
                errorType,
                errorMessage,
            });
    }

    private static void RejectsHarnessResult(
        string json,
        string expectedCaseId,
        string expectedMessage,
        string name)
    {
        var exception = Throws<ExecutableHarnessResultValidationException>(
            () => ExecutableHarnessResultParser.Parse(
                CaptureToolText(json),
                CaptureToolText(string.Empty),
                expectedCaseId),
            name);
        Contains(expectedMessage, exception.Message, $"{name} message");
    }
}
