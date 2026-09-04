using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Doccer.TestRunner.Tests;

internal static partial class Program
{
    private static void FinalizedRunRetentionKeepsTheNewestSixteen()
    {
        var repositoryRoot = CreateContainedTestRoot("retain-order");
        try
        {
            var origin = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var runs = Enumerable.Range(0, 18)
                .Select(index => SeedFinalizedRun(
                    repositoryRoot,
                    origin.AddMinutes(index),
                    withDetails: index == 0))
                .ToArray();

            var pruned = FinalizedRunPruner.Prune(runs[^1].Artifacts, runs[^1].Summary);

            Equal(2, pruned, "retention prune count");
            True(!Directory.Exists(runs[0].Artifacts.RunDirectory), "oldest finalized run pruned");
            True(!Directory.Exists(runs[1].Artifacts.RunDirectory), "second-oldest finalized run pruned");
            True(Directory.Exists(runs[2].Artifacts.RunDirectory), "newest retained boundary exists");
            True(Directory.Exists(runs[^1].Artifacts.RunDirectory), "current finalized run retained");
            Equal(
                TestRunRetentionContract.MaximumFinalizedRunCount,
                Directory.EnumerateDirectories(
                        runs[^1].Artifacts.GeneratedRoot,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Count(),
                "retention finalized directory bound");
            Equal(
                0,
                FinalizedRunPruner.Prune(runs[^1].Artifacts, runs[^1].Summary),
                "retention pruning is idempotent");
        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, repositoryRoot);
        }
    }

    private static void FinalizedRunRetentionPreservesActiveAndUnrecognizedDirectories()
    {
        var repositoryRoot = CreateContainedTestRoot("retain-safe");
        try
        {
            var origin = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
            var runs = Enumerable.Range(0, 17)
                .Select(index => SeedFinalizedRun(repositoryRoot, origin.AddMinutes(index)))
                .ToArray();
            var generatedRoot = runs[^1].Artifacts.GeneratedRoot;
            var oldest = runs[0];
            var oldestShortId = oldest.Summary.RunId[..16];
            var claimPath = Path.Combine(generatedRoot, $".{oldestShortId}.claim");

            var malformed = CreateMalformedRunDirectory(
                generatedRoot,
                origin.AddDays(-2),
                includeSummary: true);
            var partial = CreateMalformedRunDirectory(
                generatedRoot,
                origin.AddDays(-1),
                includeSummary: false);
            var duplicateSummary = SeedFinalizedRun(repositoryRoot, origin.AddDays(-3));
            var duplicateJson = duplicateSummary.Summary.ToJson();
            File.WriteAllText(
                duplicateSummary.Artifacts.SummaryPath,
                duplicateJson.Insert(1, "\"schemaVersion\":1,"));
            var missingEvidence = SeedFinalizedRun(repositoryRoot, origin.AddDays(-4));
            File.Delete(missingEvidence.Artifacts.EventsPath);
            var noncanonical = Path.Combine(generatedRoot, "operator-notes");
            Directory.CreateDirectory(noncanonical);
            File.WriteAllText(Path.Combine(noncanonical, "summary.json"), oldest.Summary.ToJson());

            using (var claim = new FileStream(
                       claimPath,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       bufferSize: 1,
                       FileOptions.DeleteOnClose))
            {
                Equal(
                    0,
                    FinalizedRunPruner.Prune(runs[^1].Artifacts, runs[^1].Summary),
                    "actively claimed finalized run is not pruned");
                True(
                    Directory.Exists(oldest.Artifacts.RunDirectory),
                    "active finalized run remains present");
            }

            Equal(
                1,
                FinalizedRunPruner.Prune(runs[^1].Artifacts, runs[^1].Summary),
                "released old finalized run is pruned");
            True(
                !Directory.Exists(oldest.Artifacts.RunDirectory),
                "released old finalized run was removed");
            True(Directory.Exists(malformed), "malformed run directory is preserved");
            True(Directory.Exists(partial), "partial run directory is preserved");
            True(
                Directory.Exists(duplicateSummary.Artifacts.RunDirectory),
                "duplicate-property summary is preserved");
            True(
                Directory.Exists(missingEvidence.Artifacts.RunDirectory),
                "run missing event evidence is preserved");
            True(Directory.Exists(noncanonical), "noncanonical directory is preserved");
        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, repositoryRoot);
        }
    }

    private static void FinalizedRunRetentionSerializesConcurrentPruners()
    {
        var repositoryRoot = CreateContainedTestRoot("retain-race");
        try
        {
            var origin = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
            var runs = Enumerable.Range(0, 18)
                .Select(index => SeedFinalizedRun(repositoryRoot, origin.AddMinutes(index)))
                .ToArray();
            using var start = new ManualResetEventSlim(initialState: false);
            var attempts = Enumerable.Range(0, 2)
                .Select(_ => Task.Run(() =>
                {
                    start.Wait();
                    return FinalizedRunPruner.Prune(runs[^1].Artifacts, runs[^1].Summary);
                }))
                .ToArray();

            start.Set();
            var counts = Task.WhenAll(attempts).GetAwaiter().GetResult();

            Equal(2, counts.Sum(), "concurrent retention aggregate prune count");
            Equal(1, counts.Count(count => count == 2), "one concurrent retention owner prunes");
            Equal(
                1,
                counts.Count(count => count == 0),
                "second concurrent retention owner is idempotent");
            Equal(
                TestRunRetentionContract.MaximumFinalizedRunCount,
                Directory.EnumerateDirectories(
                        runs[^1].Artifacts.GeneratedRoot,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Count(),
                "concurrent retention finalized directory bound");
        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, repositoryRoot);
        }
    }

    private static void CompletedRunReceiptReportsRetentionPruning()
    {
        var repositoryRoot = CreateContainedTestRoot("retain-live");
        try
        {
            var origin = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var runs = Enumerable.Range(0, TestRunRetentionContract.MaximumFinalizedRunCount)
                .Select(index => SeedFinalizedRun(repositoryRoot, origin.AddMinutes(index)))
                .ToArray();

            var invocation = RunFakeChildPlan(
                repositoryRoot,
                "retention-live",
                Array.Empty<string>());
            var completed = ReadCompletedRun(
                repositoryRoot,
                invocation,
                expectedPrunedRunCount: 1);

            True(
                !Directory.Exists(runs[0].Artifacts.RunDirectory),
                "integrated retention prunes the oldest finalized run");
            True(Directory.Exists(completed.RunDirectory), "integrated retention keeps current run");
            Equal(
                TestRunRetentionContract.MaximumFinalizedRunCount,
                Directory.EnumerateDirectories(
                        runs[^1].Artifacts.GeneratedRoot,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Count(),
                "integrated retention restores the finalized run bound");
        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, repositoryRoot);
        }
    }

    private static void CurrentRunClaimLivesThroughReceiptWriting()
    {
        var repositoryRoot = CreateContainedTestRoot("retain-claim");
        try
        {
            var planFileName = "claim-order.json";
            File.WriteAllText(
                Path.Combine(repositoryRoot, planFileName),
                CreateCommandPlanJson(
                    "claim-order",
                    new[]
                    {
                        "exec",
                        typeof(global::Doccer.TestRunner.FakeChild.Program).Assembly.Location,
                    }));
            var stdout = new ClaimObservingWriter(repositoryRoot);
            var stderr = new StringWriter();

            var exitCode = global::Doccer.TestRunner.Program.RunAsync(
                    new[]
                    {
                        "run",
                        "--plan", planFileName,
                        "--configuration", "Debug",
                        "--max-parallel", "1",
                    },
                    stdout,
                    stderr,
                    default,
                    repositoryRoot)
                .GetAwaiter()
                .GetResult();

            Equal(0, exitCode, "claim lifetime run exit");
            Equal(string.Empty, stderr.ToString(), "claim lifetime parent stderr");
            True(stdout.ObservedLockedClaim, "current run claim remains locked during receipt write");
            True(
                stdout.ObservedClaimPath is not null &&
                !File.Exists(stdout.ObservedClaimPath),
                "current run claim is removed after receipt write");
        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, repositoryRoot);
        }
    }

    private static SeededFinalizedRun SeedFinalizedRun(
        string repositoryRoot,
        DateTimeOffset requestedAtUtc,
        bool withDetails = false)
    {
        var identity = Guid.NewGuid();
        var runId = identity.ToString("N");
        var runDirectory = Path.Combine(
            repositoryRoot,
            "build",
            "test-runs",
            TestArtifactLayout.CreateRunDirectoryName(requestedAtUtc, identity));
        Directory.CreateDirectory(runDirectory);
        var item = new TestWorkItem(
            "seed.case",
            "Seed case",
            TestWorkItemKind.Command,
            "seed.case",
            SourceProject: null,
            "dotnet",
            Array.Empty<string>(),
            ".",
            new Dictionary<string, string>(),
            TimeoutMilliseconds: null,
            "retention-contract",
            TestConcurrency.Parallel);
        var artifacts = TestArtifactLayout.Create(
            repositoryRoot,
            runDirectory,
            new[] { item });
        var plan = TestRunPlanSnapshot.Create(
            runId,
            requestedAtUtc,
            "Debug",
            maxParallel: 1,
            new[] { item },
            artifacts);
        var startedAtUtc = requestedAtUtc.AddMilliseconds(10);
        var completedAtUtc = requestedAtUtc.AddMilliseconds(20);
        var result = TestExecutionResult.Create(
            runId,
            item.Id,
            TestExecutionStatus.Passed,
            startedAtUtc,
            completedAtUtc,
            processExitCode: 0,
            assertionCount: 1,
            standardOutput: withDetails ? TestStreamCapture.Create(4) : null);
        var summary = TestRunSummary.Create(
            plan,
            startedAtUtc,
            completedAtUtc,
            new[] { result });

        File.WriteAllText(artifacts.PlanPath, plan.ToJson());
        if (withDetails)
        {
            var caseArtifacts = artifacts.Cases.Single();
            Directory.CreateDirectory(caseArtifacts.CaseDirectory);
            File.WriteAllText(caseArtifacts.StandardOutputPath, "seed");
            File.WriteAllText(caseArtifacts.ResultPath, result.ToJson());
        }

        File.WriteAllLines(
            artifacts.EventsPath,
            new[]
            {
                TestRunEvent.Create(
                    runId,
                    sequence: 1,
                    startedAtUtc,
                    TestRunEventKind.RunStarted).ToJsonLine(),
                TestRunEvent.Create(
                    runId,
                    sequence: 2,
                    startedAtUtc,
                    TestRunEventKind.ItemStarted,
                    item.Id).ToJsonLine(),
                TestRunEvent.Create(
                    runId,
                    sequence: 3,
                    completedAtUtc,
                    TestRunEventKind.ItemFinished,
                    item.Id,
                    TestExecutionStatus.Passed).ToJsonLine(),
                TestRunEvent.Create(
                    runId,
                    sequence: 4,
                    completedAtUtc,
                    TestRunEventKind.RunFinished).ToJsonLine(),
            });
        File.WriteAllText(artifacts.SummaryPath, summary.ToJson());
        return new SeededFinalizedRun(artifacts, summary);
    }

    private static string CreateMalformedRunDirectory(
        string generatedRoot,
        DateTimeOffset requestedAtUtc,
        bool includeSummary)
    {
        var directory = Path.Combine(
            generatedRoot,
            TestArtifactLayout.CreateRunDirectoryName(requestedAtUtc, Guid.NewGuid()));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plan.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "events.jsonl"), "{}\n");
        if (includeSummary)
        {
            File.WriteAllText(Path.Combine(directory, "summary.json"), "{}");
        }

        return directory;
    }

    private sealed record SeededFinalizedRun(
        TestArtifactLayout Artifacts,
        TestRunSummary Summary);

    private sealed class ClaimObservingWriter : StringWriter
    {
        private readonly string _repositoryRoot;

        public ClaimObservingWriter(string repositoryRoot)
        {
            _repositoryRoot = repositoryRoot;
        }

        public bool ObservedLockedClaim { get; private set; }

        public string? ObservedClaimPath { get; private set; }

        public override System.Threading.Tasks.Task WriteLineAsync(string? value)
        {
            var receipt = TestRunnerJson.Deserialize<TestRunReceipt>(value ?? string.Empty)
                ?? throw new InvalidOperationException("The observed receipt did not deserialize.");
            ObservedClaimPath = Path.Combine(
                _repositoryRoot,
                "build",
                "test-runs",
                $".{receipt.RunId[..16]}.claim");
            if (!File.Exists(ObservedClaimPath))
            {
                return base.WriteLineAsync(value);
            }

            try
            {
                using var claim = new FileStream(
                    ObservedClaimPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
            }
            catch (IOException)
            {
                ObservedLockedClaim = true;
            }

            return base.WriteLineAsync(value);
        }
    }
}
