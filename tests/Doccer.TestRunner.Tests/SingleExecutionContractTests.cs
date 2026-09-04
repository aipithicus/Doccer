using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Doccer.TestRunner.Tests;

internal static partial class Program
{
    private static void BoundedProcessCaptureRetainsHeadAndTail()
    {
        var source = new byte[TestCaptureContract.MaximumRetainedBytesPerStream + 4096];
        for (var index = 0; index < source.Length; index++)
        {
            source[index] = (byte)(index % 251);
        }

        using var stream = new MemoryStream(source, writable: false);
        var capture = BoundedProcessStreamCapture.CaptureAsync(stream)
            .GetAwaiter()
            .GetResult();
        True(capture.Evidence is not null, "bounded capture metadata exists");
        Equal((long)source.Length, capture.Evidence!.ObservedBytes, "bounded capture observed bytes");
        Equal(
            TestCaptureContract.MaximumRetainedBytesPerStream,
            capture.Evidence.RetainedBytes,
            "bounded capture retained bytes");
        Equal(true, capture.Evidence.Truncated, "bounded capture truncation residue");
        True(capture.Content is not null, "bounded capture content exists");
        var content = capture.Content!;
        True(
            content.AsSpan(0, (int)TestCaptureContract.RetainedHeadBytesPerTruncatedStream)
                .SequenceEqual(source.AsSpan(
                    0,
                    (int)TestCaptureContract.RetainedHeadBytesPerTruncatedStream)),
            "bounded capture exact head");
        True(
            content.AsSpan(content.Length - (int)TestCaptureContract.RetainedTailBytesPerTruncatedStream)
                .SequenceEqual(source.AsSpan(
                    source.Length - (int)TestCaptureContract.RetainedTailBytesPerTruncatedStream)),
            "bounded capture exact tail");
        Contains(
            "stream truncated",
            Encoding.UTF8.GetString(content),
            "bounded capture marker");

        using var shortStream = new MemoryStream(new byte[] { 1, 2, 3 }, writable: false);
        var shortCapture = BoundedProcessStreamCapture.CaptureAsync(shortStream)
            .GetAwaiter()
            .GetResult();
        Equal(false, shortCapture.Evidence?.Truncated, "short capture is complete");
        True(
            shortCapture.Content!.AsSpan().SequenceEqual(new byte[] { 1, 2, 3 }),
            "short capture bytes");
    }

    private static void ArtifactInspectionEnforcesPhysicalBudgets()
    {
        var testRoot = CreateContainedTestRoot("artifact");
        try
        {
            var item = TestPlanParser.Parse(
                CreateCommandPlanJson("artifact.case", Array.Empty<string>()),
                testRoot).Entries.Single();
            var workItem = CommandPlanExpander.Expand(
                new TestPlan(
                    TestRunnerProtocol.SchemaVersion,
                    TestRunnerProtocol.Plan,
                    new[] { item })).Commands.Single();
            var layout = TestArtifactLayout.Create(
                testRoot,
                "build/test-runs/artifact-inspection",
                new[] { workItem });
            var caseLayout = layout.Cases.Single();
            Directory.CreateDirectory(caseLayout.ArtifactDirectory);
            for (var index = 0; index < TestCaptureContract.MaximumArtifactFileCount; index++)
            {
                File.WriteAllText(
                    Path.Combine(caseLayout.ArtifactDirectory, $"e{index}.txt"),
                    "evidence");
            }

            var capture = ArtifactCollectionInspector.Inspect(caseLayout);
            Equal(
                TestCaptureContract.MaximumArtifactFileCount,
                capture?.FileCount,
                "artifact inspector accepted file count");
            File.WriteAllText(
                Path.Combine(caseLayout.ArtifactDirectory, "overflow.txt"),
                "overflow");
            Throws<TestArtifactValidationException>(
                () => ArtifactCollectionInspector.Inspect(caseLayout),
                "artifact inspector file count rejection");
        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, testRoot);
        }
    }

    private static void SingleCommandExecutionWritesProgressiveEvidence()
    {
        var testRoot = CreateContainedTestRoot("execute");
        try
        {
            var clean = RunFakeChildPlan(testRoot, "clean", Array.Empty<string>());
            Equal(0, clean.ExitCode, "clean run exit code");
            var cleanEvidence = ReadCompletedRun(testRoot, clean);
            Equal(TestRunOutcome.Passed, cleanEvidence.Receipt.Outcome, "clean receipt outcome");
            Equal(1, cleanEvidence.Summary.Counts.Passed, "clean summary pass count");
            Equal(0, cleanEvidence.Summary.Details.Count, "clean summary detail count");
            True(
                !Directory.Exists(Path.Combine(cleanEvidence.RunDirectory, "c")),
                "clean pass omits case directory");
            Equal(4, cleanEvidence.Events.Count, "clean lifecycle event count");
            Equal(TestRunEventKind.RunStarted, cleanEvidence.Events[0].Kind, "clean run-started event");
            Equal(TestRunEventKind.ItemStarted, cleanEvidence.Events[1].Kind, "clean item-started event");
            Equal(TestRunEventKind.ItemFinished, cleanEvidence.Events[2].Kind, "clean item-finished event");
            Equal(TestRunEventKind.RunFinished, cleanEvidence.Events[3].Kind, "clean run-finished event");
            Contains("\"maxParallel\": 1", cleanEvidence.PlanJson, "single run frozen parallelism");

            var chatty = RunFakeChildPlan(
                testRoot,
                "chatty",
                new[]
                {
                    "--stdout", "visible only in stdout evidence",
                    "--stderr", "visible only in stderr evidence",
                    "--artifact", "nested/evidence.txt",
                    "--artifact-content", "contained artifact",
                });
            Equal(0, chatty.ExitCode, "chatty run exit code");
            True(
                !chatty.StandardOutput.Contains("visible only", StringComparison.Ordinal),
                "child stdout is not relayed");
            Equal(string.Empty, chatty.StandardError, "chatty parent stderr");
            var chattyEvidence = ReadCompletedRun(testRoot, chatty);
            Equal(1, chattyEvidence.Summary.Details.Count, "chatty detail reference");
            var chattyResultPath = RepositoryPaths.ResolveContained(
                testRoot,
                chattyEvidence.Summary.Details.Single().ResultPath,
                "chattyResultPath",
                allowRoot: false);
            var chattyCaseDirectory = Path.GetDirectoryName(chattyResultPath)!;
            Contains(
                "visible only in stdout evidence",
                File.ReadAllText(Path.Combine(chattyCaseDirectory, "out.log")),
                "captured stdout evidence");
            Contains(
                "visible only in stderr evidence",
                File.ReadAllText(Path.Combine(chattyCaseDirectory, "err.log")),
                "captured stderr evidence");
            Equal(
                "contained artifact",
                File.ReadAllText(Path.Combine(chattyCaseDirectory, "a", "nested", "evidence.txt")),
                "child artifact evidence");
            True(!Directory.Exists(Path.Combine(chattyCaseDirectory, "tmp")), "chatty temp removed");

            var flooded = RunFakeChildPlan(
                testRoot,
                "flooded",
                new[] { "--stdout-bytes", "307200" });
            Equal(0, flooded.ExitCode, "flooded run exit code");
            True(
                flooded.StandardOutput.Length <= TestRunReceipt.MaximumConsoleLineLength + 2,
                "flooded child does not expand parent console output");
            var floodedEvidence = ReadCompletedRun(testRoot, flooded);
            var floodedResult = floodedEvidence.Summary.Results.Single();
            Equal(true, floodedResult.StandardOutput?.Truncated, "flooded stream truncation status");
            Equal(
                TestCaptureContract.MaximumRetainedBytesPerStream,
                floodedResult.StandardOutput?.RetainedBytes,
                "flooded stream retained byte cap");
            var floodedResultPath = RepositoryPaths.ResolveContained(
                testRoot,
                floodedEvidence.Summary.Details.Single().ResultPath,
                "floodedResultPath",
                allowRoot: false);
            var floodedLog = Path.Combine(Path.GetDirectoryName(floodedResultPath)!, "out.log");
            True(
                new FileInfo(floodedLog).Length < floodedResult.StandardOutput!.ObservedBytes,
                "flooded log is physically bounded");
            Contains("stream truncated", File.ReadAllText(floodedLog), "flooded log marker");

            var failed = RunFakeChildPlan(
                testRoot,
                "failed",
                new[] { "--exit-code", "7" });
            Equal(1, failed.ExitCode, "failed run exit code");
            var failedEvidence = ReadCompletedRun(testRoot, failed);
            Equal(TestRunOutcome.Failed, failedEvidence.Receipt.Outcome, "failed receipt outcome");
            Equal(7, failedEvidence.Summary.Results.Single().ProcessExitCode, "failed process exit");
            Equal(1, failedEvidence.Summary.Details.Count, "failed result detail");

            var timeoutWatch = Stopwatch.StartNew();
            var timedOut = RunFakeChildPlan(
                testRoot,
                "timeout",
                new[]
                {
                    "--delay-milliseconds", "30000",
                    "--spawn-child-milliseconds", "30000",
                    "--ignore-cancel",
                },
                timeoutMilliseconds: 250);
            timeoutWatch.Stop();
            Equal(3, timedOut.ExitCode, "timeout run exit code");
            True(timeoutWatch.Elapsed < TimeSpan.FromSeconds(10), "timeout kills process tree promptly");
            var timeoutEvidence = ReadCompletedRun(testRoot, timedOut);
            Equal(TestExecutionStatus.TimedOut, timeoutEvidence.Summary.Results.Single().Status, "timeout status");

            using var cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(250);
            var canceled = RunFakeChildPlan(
                testRoot,
                "canceled",
                new[] { "--delay-milliseconds", "30000", "--ignore-cancel" },
                timeoutMilliseconds: null,
                cancellation.Token);
            Equal(3, canceled.ExitCode, "canceled run exit code");
            var canceledEvidence = ReadCompletedRun(testRoot, canceled);
            Equal(TestExecutionStatus.Canceled, canceledEvidence.Summary.Results.Single().Status, "canceled status");
            Equal(
                1,
                canceledEvidence.Events.Count(item => item.Kind == TestRunEventKind.CancellationRequested),
                "cancellation event count");

            var missingExecutable = RunCommandPlan(
                testRoot,
                "missing",
                executable: $"doccer-command-not-found-{Guid.NewGuid():N}",
                arguments: Array.Empty<string>(),
                timeoutMilliseconds: 1000,
                CancellationToken.None);
            Equal(4, missingExecutable.ExitCode, "launch infrastructure exit code");
            var missingEvidence = ReadCompletedRun(testRoot, missingExecutable);
            Equal(
                TestExecutionStatus.InfrastructureError,
                missingEvidence.Summary.Results.Single().Status,
                "launch infrastructure status");
            True(
                !Directory.Exists(Path.Combine(
                    Path.GetDirectoryName(
                        RepositoryPaths.ResolveContained(
                            testRoot,
                            missingEvidence.Summary.Details.Single().ResultPath,
                            "missingResultPath",
                            allowRoot: false))!,
                    "tmp")),
                "launch failure temp removed");

        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, testRoot);
        }
    }

    private static void MultipleCommandExecutionUsesBoundedExclusiveScheduling()
    {
        var testRoot = CreateContainedTestRoot("multi");
        try
        {
            File.WriteAllText(
                Path.Combine(testRoot, "multiple.json"),
                CreateMultipleCommandPlanJson());
            var invocation = InvokeRunner(
                testRoot,
                new[]
                {
                    "run",
                    "--plan", "multiple.json",
                    "--max-parallel", "2",
                },
                CancellationToken.None);
            Equal(0, invocation.ExitCode, "multiple command run exit");
            var completed = ReadCompletedRun(testRoot, invocation);
            Equal(5, completed.Summary.Counts.Passed, "multiple command pass count");
            Equal(0, completed.Summary.Details.Count, "multiple clean commands remain summary-only");
            Contains("\"maxParallel\": 2", completed.PlanJson, "frozen scheduler bound");
            SequenceEqual(
                new[]
                {
                    "a.parallel",
                    "b.parallel",
                    "m.exclusive",
                    "y.parallel",
                    "z.parallel",
                },
                completed.Summary.Results.Select(result => result.WorkItemId).ToArray(),
                "multiple command summary order");

            var active = new HashSet<string>(StringComparer.Ordinal);
            var maximumActive = 0;
            foreach (var itemEvent in completed.Events.Where(
                         item => item.Kind is TestRunEventKind.ItemStarted or
                             TestRunEventKind.ItemFinished))
            {
                if (itemEvent.Kind == TestRunEventKind.ItemStarted)
                {
                    if (itemEvent.WorkItemId == "m.exclusive")
                    {
                        Equal(0, active.Count, "exclusive integration drain");
                    }
                    else
                    {
                        True(
                            !active.Contains("m.exclusive"),
                            "parallel integration excludes exclusive overlap");
                    }

                    True(active.Add(itemEvent.WorkItemId!), "integration item starts once");
                    maximumActive = Math.Max(maximumActive, active.Count);
                }
                else
                {
                    True(active.Remove(itemEvent.WorkItemId!), "integration item finishes active");
                }
            }

            Equal(0, active.Count, "integration scheduler drains all items");
            Equal(2, maximumActive, "integration scheduler reaches configured bound");
            True(
                !Directory.Exists(Path.Combine(completed.RunDirectory, "c")),
                "multiple clean commands omit case directory");
        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, testRoot);
        }
    }

    private static void CancellationAccountsForEveryUnstartedItem()
    {
        var testRoot = CreateContainedTestRoot("multi-cancel");
        try
        {
            File.WriteAllText(
                Path.Combine(testRoot, "canceled.json"),
                CreateMultipleCommandPlanJson());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var invocation = InvokeRunner(
                testRoot,
                new[]
                {
                    "run",
                    "--plan", "canceled.json",
                    "--max-parallel", "2",
                },
                cancellation.Token);
            Equal(3, invocation.ExitCode, "pre-canceled multiple command exit");
            var completed = ReadCompletedRun(testRoot, invocation);
            Equal(5, completed.Summary.Counts.NotStarted, "pre-canceled not-started count");
            Equal(0, completed.Summary.Counts.Canceled, "pre-canceled active count");
            Equal(
                0,
                completed.Events.Count(item => item.Kind == TestRunEventKind.ItemStarted),
                "pre-canceled starts no items");
            Equal(
                5,
                completed.Events.Count(item =>
                    item.Kind == TestRunEventKind.ItemFinished &&
                    item.Status == TestExecutionStatus.NotStarted),
                "pre-canceled terminal event accounting");
            Equal(
                1,
                completed.Events.Count(item => item.Kind == TestRunEventKind.CancellationRequested),
                "pre-canceled run records one cancellation event");
        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, testRoot);
        }
    }

    private static void HarnessExpansionFailuresBeforeWriting()
    {
        var invalidRoot = CreateContainedTestRoot("invalid");
        try
        {

            File.WriteAllText(
                Path.Combine(invalidRoot, "missing-harness.json"),
                CreateHarnessOnlyPlanJson());
            var missingHarness = InvokeRunner(
                invalidRoot,
                new[] { "run", "--plan", "missing-harness.json" },
                CancellationToken.None);
            Equal(4, missingHarness.ExitCode, "missing harness project exit");
            Equal(string.Empty, missingHarness.StandardOutput, "missing harness project stdout");
            Contains(
                "could not be expanded",
                missingHarness.StandardError,
                "missing harness project diagnostic");
            True(
                !Directory.Exists(Path.Combine(invalidRoot, "build", "test-runs")),
                "harness expansion failure creates no run root");
            True(
                !Directory.EnumerateDirectories(
                        Path.Combine(invalidRoot, "build"),
                        "hx-*",
                        SearchOption.TopDirectoryOnly)
                    .Any(),
                "harness expansion failure removes its workspace");
        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, invalidRoot);
        }
    }

    private static RunnerInvocation RunFakeChildPlan(
        string repositoryRoot,
        string name,
        IReadOnlyList<string> childArguments,
        int? timeoutMilliseconds = 5000,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "exec",
            typeof(global::Doccer.TestRunner.FakeChild.Program).Assembly.Location,
        };
        arguments.AddRange(childArguments);
        return RunCommandPlan(
            repositoryRoot,
            name,
            "dotnet",
            arguments,
            timeoutMilliseconds,
            cancellationToken);
    }

    private static RunnerInvocation RunCommandPlan(
        string repositoryRoot,
        string name,
        string executable,
        IReadOnlyList<string> arguments,
        int? timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var planFileName = $"{name}.json";
        File.WriteAllText(
            Path.Combine(repositoryRoot, planFileName),
            CreateCommandPlanJson(name, arguments, executable, timeoutMilliseconds));
        return InvokeRunner(
            repositoryRoot,
            new[]
            {
                "run",
                "--plan", planFileName,
                "--configuration", "Debug",
                "--max-parallel", "1",
            },
            cancellationToken);
    }

    private static string CreateMultipleCommandPlanJson()
    {
        var assemblyPath = typeof(global::Doccer.TestRunner.FakeChild.Program).Assembly.Location;
        var entries = new[]
        {
            new CommandPlanSpec("z.parallel", "parallel", 80),
            new CommandPlanSpec("a.parallel", "parallel", 160),
            new CommandPlanSpec("m.exclusive", "exclusive", 30),
            new CommandPlanSpec("y.parallel", "parallel", 80),
            new CommandPlanSpec("b.parallel", "parallel", 160),
        }
            .Select(spec => new
            {
                id = spec.Id,
                displayName = spec.Id,
                kind = "command",
                executable = "dotnet",
                arguments = new[]
                {
                    "exec",
                    assemblyPath,
                    "--delay-milliseconds",
                    spec.DelayMilliseconds.ToString(),
                },
                workingDirectory = ".",
                environment = new Dictionary<string, string>(),
                timeoutMilliseconds = 5000,
                group = "scheduler-contract",
                concurrency = spec.Concurrency,
            })
            .ToArray();
        return JsonSerializer.Serialize(
            new
            {
                schemaVersion = TestRunnerProtocol.SchemaVersion,
                protocol = TestRunnerProtocol.Plan,
                entries,
            },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static string CreateCommandPlanJson(
        string id,
        IReadOnlyList<string> arguments,
        string executable = "dotnet",
        int? timeoutMilliseconds = 5000)
    {
        return JsonSerializer.Serialize(
            new
            {
                schemaVersion = TestRunnerProtocol.SchemaVersion,
                protocol = TestRunnerProtocol.Plan,
                entries = new[]
                {
                    new
                    {
                        id,
                        displayName = id,
                        kind = "command",
                        executable,
                        arguments,
                        workingDirectory = ".",
                        environment = new Dictionary<string, string>(),
                        timeoutMilliseconds,
                        group = "runner-contract",
                        concurrency = "parallel",
                    },
                },
            },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static RunnerInvocation InvokeRunner(
        string repositoryRoot,
        string[] args,
        CancellationToken cancellationToken)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = global::Doccer.TestRunner.Program.RunAsync(
                args,
                stdout,
                stderr,
                cancellationToken,
                repositoryRoot)
            .GetAwaiter()
            .GetResult();
        return new RunnerInvocation(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static CompletedRunEvidence ReadCompletedRun(
        string repositoryRoot,
        RunnerInvocation invocation,
        int expectedPrunedRunCount = 0)
    {
        Equal(string.Empty, invocation.StandardError, "completed run parent stderr");
        var lines = invocation.StandardOutput.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
        Equal(1, lines.Length, "completed run receipt line count");
        True(lines[0].Length <= TestRunReceipt.MaximumConsoleLineLength, "completed receipt bound");
        var receipt = TestRunnerJson.Deserialize<TestRunReceipt>(lines[0])
            ?? throw new InvalidOperationException("The receipt did not deserialize.");
        var summaryPath = RepositoryPaths.ResolveContained(
            repositoryRoot,
            receipt.SummaryPath,
            nameof(receipt.SummaryPath),
            allowRoot: false);
        var runDirectory = Path.GetDirectoryName(summaryPath)
            ?? throw new InvalidOperationException("The summary path has no run directory.");
        var summary = TestRunnerJson.Deserialize<TestRunSummary>(File.ReadAllText(summaryPath))
            ?? throw new InvalidOperationException("The summary did not deserialize.");
        var events = File.ReadAllLines(Path.Combine(runDirectory, "events.jsonl"))
            .Select(line => TestRunnerJson.Deserialize<TestRunEvent>(line)
                ?? throw new InvalidOperationException("An event did not deserialize."))
            .ToArray();
        for (var index = 0; index < events.Length; index++)
        {
            Equal(index + 1L, events[index].Sequence, $"event sequence {index + 1}");
        }

        Equal(receipt.RunId, summary.RunId, "receipt summary run identity");
        Equal(invocation.ExitCode, receipt.ExitCode, "receipt process exit code");
        Equal(
            expectedPrunedRunCount,
            receipt.PrunedRunCount,
            "completed run prune count");
        True(File.Exists(Path.Combine(runDirectory, "plan.json")), "frozen run plan exists");
        True(File.Exists(Path.Combine(runDirectory, "events.jsonl")), "event stream exists");
        True(File.Exists(summaryPath), "summary exists");
        True(
            Directory.EnumerateFiles(runDirectory, "*.tmp", SearchOption.AllDirectories).Count() == 0,
            "no runner temporary evidence files remain");
        True(
            Directory.EnumerateDirectories(runDirectory, "tmp", SearchOption.AllDirectories).Count() == 0,
            "no case temp directories remain");
        True(
            Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "build", "test-runs"),
                "*.claim",
                SearchOption.TopDirectoryOnly).Count() == 0,
            "no run reservation claims remain");
        return new CompletedRunEvidence(
            receipt,
            summary,
            events,
            runDirectory,
            File.ReadAllText(Path.Combine(runDirectory, "plan.json")));
    }

    private static string CreateContainedTestRoot(string purpose)
    {
        var directoryName = $"r-{purpose}-{Guid.NewGuid():N}";
        directoryName = directoryName[..Math.Min(28, directoryName.Length)];
        var root = Path.Combine(AppContext.BaseDirectory, directoryName);
        True(
            RepositoryPaths.Contains(AppContext.BaseDirectory, root, allowRoot: false),
            $"{purpose} root remains in repository build output");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed record RunnerInvocation(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed record CompletedRunEvidence(
        TestRunReceipt Receipt,
        TestRunSummary Summary,
        IReadOnlyList<TestRunEvent> Events,
        string RunDirectory,
        string PlanJson);

    private sealed record CommandPlanSpec(
        string Id,
        string Concurrency,
        int DelayMilliseconds);
}
