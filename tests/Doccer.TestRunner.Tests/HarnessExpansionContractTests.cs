using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Doccer.TestRunner.Tests;

internal static partial class Program
{
    private static void ExecutableHarnessCatalogSchemaIsStrict()
    {
        var catalog = ExecutableHarnessCatalogParser.Parse(ValidHarnessCatalogJson);

        Equal(1, catalog.SchemaVersion, "harness catalog schema version");
        Equal("doccer-test-harness", catalog.Protocol, "harness catalog protocol");
        Equal("fake.contracts", catalog.SuiteId, "harness catalog suite identity");
        Equal(2, catalog.Cases.Count, "harness catalog case count");
        Equal(1, catalog.Cases[0].Ordinal, "harness catalog first ordinal");
        Equal("beta", catalog.Cases[0].Id, "harness catalog first identity");
        Equal(TestConcurrency.Exclusive, catalog.Cases[0].Concurrency, "exclusive catalog posture");
        Equal(TestConcurrency.Parallel, catalog.Cases[1].Concurrency, "parallel catalog posture");
        Throws<NotSupportedException>(
            () => ((ICollection<ExecutableHarnessCatalogCase>)catalog.Cases).Add(catalog.Cases[0]),
            "harness catalog cases are frozen");

        RejectsHarnessCatalog(
            ValidHarnessCatalogJson.Replace(
                "\"schemaVersion\": 1",
                "\"schemaVersion\": 2",
                StringComparison.Ordinal),
            "Unsupported executable-harness catalog schema version",
            "harness catalog schema rejection");
        RejectsHarnessCatalog(
            ValidHarnessCatalogJson.Replace(
                "\"protocol\": \"doccer-test-harness\"",
                "\"protocol\": \"other\"",
                StringComparison.Ordinal),
            "protocol must be",
            "harness catalog protocol rejection");
        RejectsHarnessCatalog(
            ValidHarnessCatalogJson.Replace(
                "\"ordinal\": 2",
                "\"ordinal\": 3",
                StringComparison.Ordinal),
            "must declare ordinal 2",
            "harness catalog ordinal gap rejection");
        RejectsHarnessCatalog(
            ValidHarnessCatalogJson.Replace(
                "\"id\": \"alpha\"",
                "\"id\": \"beta\"",
                StringComparison.Ordinal),
            "Duplicate executable-harness case ID",
            "harness catalog duplicate identity rejection");
        RejectsHarnessCatalog(
            ValidHarnessCatalogJson.Replace(
                "\"concurrency\": \"parallel\"",
                "\"concurrency\": \"Parallel\"",
                StringComparison.Ordinal),
            "concurrency must be",
            "harness catalog noncanonical concurrency rejection");
        RejectsHarnessCatalog(
            ValidHarnessCatalogJson.Replace(
                "\"suiteId\": \"fake.contracts\"",
                "\"suiteId\": \"fake.contracts\", \"mystery\": true",
                StringComparison.Ordinal),
            "could not be mapped",
            "harness catalog unknown property rejection");
        RejectsHarnessCatalog(
            ValidHarnessCatalogJson.Replace(
                "\"id\": \"beta\"",
                "\"id\": \"beta\", \"id\": \"duplicate\"",
                StringComparison.Ordinal),
            "Duplicate JSON property",
            "harness catalog duplicate property rejection");
        RejectsHarnessCatalog(
            """
            {
              "schemaVersion": 1,
              "protocol": "doccer-test-harness",
              "suiteId": "fake.contracts",
              "cases": []
            }
            """,
            "at least one case",
            "harness catalog empty result rejection");
        RejectsHarnessCatalog(
            "console noise\n" + ValidHarnessCatalogJson,
            "not valid schema-versioned JSON",
            "harness catalog noisy stdout rejection");
        RejectsHarnessCatalog(
            "[]",
            "must be a JSON object",
            "harness catalog nonobject rejection");
    }

    private static void ExecutableHarnessExpansionIsDeterministic()
    {
        var testRoot = CreateContainedTestRoot("expand");
        try
        {
            var projectDirectory = Path.Combine(testRoot, "tests", "FakeHarness");
            var targetDirectory = Path.Combine(testRoot, "build", "fake");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(targetDirectory);
            var projectPath = Path.Combine(projectDirectory, "FakeHarness.csproj");
            var targetPath = Path.Combine(targetDirectory, "FakeHarness.dll");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllBytes(targetPath, Array.Empty<byte>());

            var plan = TestPlanParser.Parse(CreateHarnessExpansionPlanJson(), testRoot);
            var runner = new ScriptedToolProcessRunner(
                CreateToolResult(0, "build output\n", string.Empty),
                CreateToolResult(0, targetPath + Environment.NewLine, string.Empty),
                CreateToolResult(0, ValidHarnessCatalogJson, string.Empty));
            var expansion = TestPlanExpander.ExpandAsync(
                    plan,
                    testRoot,
                    "Debug",
                    runner,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            SequenceEqual(
                new[]
                {
                    "a.command",
                    "m.harness/alpha",
                    "m.harness/beta",
                    "z.harness/alpha",
                    "z.harness/beta",
                },
                expansion.WorkItems.Select(item => item.Id).ToArray(),
                "expanded plan stable identity ordering");
            Equal(3, runner.Requests.Count, "shared harness project tool invocation count");
            SequenceEqual(
                new[]
                {
                    "build",
                    projectPath,
                    "--configuration",
                    "Debug",
                    "--nologo",
                    "--verbosity",
                    "quiet",
                },
                runner.Requests[0].Arguments,
                "harness build argument vector");
            SequenceEqual(
                new[]
                {
                    "msbuild",
                    projectPath,
                    "-nologo",
                    "-getProperty:TargetPath",
                    "-property:Configuration=Debug",
                },
                runner.Requests[1].Arguments,
                "harness target-evaluation argument vector");
            SequenceEqual(
                new[] { "exec", targetPath, "list", "--format", "json" },
                runner.Requests[2].Arguments,
                "harness discovery argument vector");
            foreach (var request in runner.Requests)
            {
                Equal("dotnet", request.FileName, "harness tool executable");
                Equal(testRoot, request.WorkingDirectory, "harness tool working directory");
                Equal(request.Environment["TEMP"], request.Environment["TMP"], "tool TEMP/TMP parity");
                Equal(
                    request.Environment["TEMP"],
                    request.Environment["TMPDIR"],
                    "tool TEMP/TMPDIR parity");
                True(
                    RepositoryPaths.Contains(
                        testRoot,
                        request.Environment["TEMP"],
                        allowRoot: false),
                    "tool temp remains in repository");
            }

            Equal(
                1,
                runner.Requests.Select(request => request.Environment["TEMP"]).Distinct().Count(),
                "one expansion workspace");
            True(
                !Directory.Exists(runner.Requests[0].Environment["TEMP"]),
                "expansion workspace removed after catalog freeze");

            var alpha = expansion.WorkItems.Single(item => item.Id == "m.harness/alpha");
            Equal(TestWorkItemKind.HarnessCase, alpha.Kind, "expanded harness work-item kind");
            Equal("m.harness", alpha.SourceId, "expanded harness source identity");
            Equal(
                "tests/FakeHarness/FakeHarness.csproj",
                alpha.SourceProject,
                "expanded harness source project");
            Equal("Alpha case", alpha.DisplayName, "expanded harness display name");
            Equal(TestConcurrency.Parallel, alpha.Concurrency, "expanded harness concurrency");
            Equal(7000, alpha.TimeoutMilliseconds, "expanded harness timeout inheritance");
            Equal("middle", alpha.Group, "expanded harness group inheritance");
            Equal("middle", alpha.Environment["SOURCE_NAME"], "expanded harness environment inheritance");
            SequenceEqual(
                new[]
                {
                    "exec",
                    targetPath,
                    "run",
                    "--case",
                    "alpha",
                    "--format",
                    "json",
                },
                alpha.Arguments,
                "expanded harness exact case argument vector");
            Equal(
                "suite~1one/case~0two~1three",
                ExecutableHarnessAdapter.CreateWorkItemId("suite/one", "case~two/three"),
                "harness composite identity escaping");
        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, testRoot);
        }
    }

    private static void CheckedInPlanExpandsLiveDoccerCatalog()
    {
        var repositoryRoot = FindRepositoryRoot();
        var buildRoot = Path.Combine(repositoryRoot, "build");
        var workspacesBefore = Directory.Exists(buildRoot)
            ? Directory.EnumerateDirectories(buildRoot, "hx-*", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileName(path)
                    ?? throw new InvalidOperationException("An expansion workspace had no name."))
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var checkedInPlanPath = Path.Combine(repositoryRoot, "tests", "test-plan.json");
        True(File.Exists(checkedInPlanPath), "checked-in test plan exists");
        var plan = TestPlanParser.Parse(File.ReadAllText(checkedInPlanPath), repositoryRoot);
        Equal(1, plan.Entries.Count, "checked-in test plan source count");
        var source = plan.Entries.Single();
        Equal("doccer.contracts", source.Id, "checked-in harness source ID");
        Equal(
            "tests/Doccer.Tests/Doccer.Tests.csproj",
            source.Project,
            "checked-in harness project");
        Equal(30000, source.TimeoutMilliseconds, "checked-in per-case timeout");
        Equal("contracts", source.Group, "checked-in harness group");

        var expansion = TestPlanExpander.ExpandAsync(
                plan,
                repositoryRoot,
                "Debug",
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Equal(114, expansion.WorkItems.Count, "checked-in Doccer catalog case count");
        Equal(
            114,
            expansion.WorkItems.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count(),
            "checked-in Doccer catalog unique expanded identities");
        Equal(
            114,
            expansion.WorkItems.Count(item => item.Concurrency == TestConcurrency.Parallel),
            "checked-in Doccer catalog parallel declarations");
        SequenceEqual(
            expansion.WorkItems.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            expansion.WorkItems.Select(item => item.Id).ToArray(),
            "checked-in Doccer catalog stable ordering");
        var firstNamedCase = expansion.WorkItems.Single(
            item => item.Id == "doccer.contracts/MasterTopologyIsTotal");
        Equal(TestWorkItemKind.HarnessCase, firstNamedCase.Kind, "checked-in Doccer work-item kind");
        Equal("dotnet", firstNamedCase.Executable, "checked-in Doccer work-item executable");
        Equal("exec", firstNamedCase.Arguments[0], "checked-in Doccer dotnet exec verb");
        True(
            RepositoryPaths.Contains(repositoryRoot, firstNamedCase.Arguments[1], allowRoot: false),
            "checked-in Doccer evaluated target remains in repository");
        True(
            File.Exists(firstNamedCase.Arguments[1]),
            "checked-in Doccer evaluated target exists");
        SequenceEqual(
            new[]
            {
                "run",
                "--case",
                "MasterTopologyIsTotal",
                "--format",
                "json",
            },
            firstNamedCase.Arguments.Skip(2).ToArray(),
            "checked-in Doccer exact selection suffix");

        var workspacesAfter = Directory.EnumerateDirectories(
                buildRoot,
                "hx-*",
                SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path)
                ?? throw new InvalidOperationException("An expansion workspace had no name."))
            .ToHashSet(StringComparer.Ordinal);
        True(
            workspacesBefore.SetEquals(workspacesAfter),
            "live catalog leaves no expansion workspace");
    }

    private static void ExecutableHarnessExpansionFailuresAreLoudAndClean()
    {
        var testRoot = CreateContainedTestRoot("expand-fail");
        try
        {
            var projectDirectory = Path.Combine(testRoot, "tests", "FakeHarness");
            var targetDirectory = Path.Combine(testRoot, "build", "fake");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(targetDirectory);
            var projectPath = Path.Combine(projectDirectory, "FakeHarness.csproj");
            var targetPath = Path.Combine(targetDirectory, "FakeHarness.dll");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllBytes(targetPath, Array.Empty<byte>());
            var plan = TestPlanParser.Parse(CreateHarnessOnlyPlanJson(), testRoot);

            var nonzero = Throws<ExecutableHarnessExpansionException>(
                () => ExpandWithScript(
                    plan,
                    testRoot,
                    CreateToolResult(0, "build output\n", string.Empty),
                    CreateToolResult(0, targetPath + Environment.NewLine, string.Empty),
                    CreateToolResult(7, string.Empty, "catalog failed")),
                "nonzero harness discovery rejection");
            Contains("exited with code 7", nonzero.Message, "nonzero discovery message");

            var truncatedBytes = new byte[TestCaptureContract.MaximumRetainedBytesPerStream + 1];
            Array.Fill(truncatedBytes, (byte)'x');
            using var truncatedStream = new MemoryStream(truncatedBytes, writable: false);
            var truncatedCapture = BoundedProcessStreamCapture.CaptureAsync(truncatedStream)
                .GetAwaiter()
                .GetResult();
            var truncated = Throws<ExecutableHarnessExpansionException>(
                () => ExpandWithScript(
                    plan,
                    testRoot,
                    CreateToolResult(0, "build output\n", string.Empty),
                    CreateToolResult(0, targetPath + Environment.NewLine, string.Empty),
                    new ToolProcessResult(
                        0,
                        truncatedCapture,
                        CaptureToolText(string.Empty))),
                "truncated harness discovery rejection");
            Contains("capture limit", truncated.Message, "truncated discovery message");

            var empty = Throws<ExecutableHarnessExpansionException>(
                () => ExpandWithScript(
                    plan,
                    testRoot,
                    CreateToolResult(0, "build output\n", string.Empty),
                    CreateToolResult(0, targetPath + Environment.NewLine, string.Empty),
                    CreateToolResult(0, string.Empty, string.Empty)),
                "empty harness discovery rejection");
            Contains("invalid catalog", empty.Message, "empty discovery message");

            var outside = Throws<ExecutableHarnessExpansionException>(
                () => ExpandWithScript(
                    plan,
                    testRoot,
                    CreateToolResult(0, "build output\n", string.Empty),
                    CreateToolResult(
                        0,
                        Path.Combine(Path.GetPathRoot(testRoot)!, "outside", "FakeHarness.dll") +
                        Environment.NewLine,
                        string.Empty)),
                "outside evaluated target rejection");
            Contains("evaluated target", outside.Message, "outside evaluated target message");

            True(
                !Directory.EnumerateDirectories(
                        Path.Combine(testRoot, "build"),
                        "hx-*",
                        SearchOption.TopDirectoryOnly)
                    .Any(),
                "failed expansions leave no workspace");
        }
        finally
        {
            RepositoryTree.DeleteContained(AppContext.BaseDirectory, testRoot);
        }
    }

    private static void RejectsHarnessCatalog(string json, string expectedMessage, string name)
    {
        var exception = Throws<ExecutableHarnessCatalogValidationException>(
            () => ExecutableHarnessCatalogParser.Parse(json),
            name);
        Contains(expectedMessage, exception.Message, $"{name} message");
    }

    private static string CreateHarnessExpansionPlanJson()
    {
        return JsonSerializer.Serialize(
            new
            {
                schemaVersion = TestRunnerProtocol.SchemaVersion,
                protocol = TestRunnerProtocol.Plan,
                entries = new object[]
                {
                    new
                    {
                        id = "z.harness",
                        displayName = "Last harness source",
                        kind = "executable_harness",
                        project = "tests/FakeHarness/FakeHarness.csproj",
                        arguments = Array.Empty<string>(),
                        workingDirectory = ".",
                        environment = new Dictionary<string, string>
                        {
                            ["SOURCE_NAME"] = "last",
                        },
                        timeoutMilliseconds = 9000,
                        group = "last",
                    },
                    new
                    {
                        id = "a.command",
                        displayName = "First command",
                        kind = "command",
                        executable = "dotnet",
                        arguments = Array.Empty<string>(),
                        workingDirectory = ".",
                        environment = new Dictionary<string, string>(),
                        timeoutMilliseconds = 5000,
                        group = "command",
                        concurrency = "parallel",
                    },
                    new
                    {
                        id = "m.harness",
                        displayName = "Middle harness source",
                        kind = "executable_harness",
                        project = "tests/FakeHarness/FakeHarness.csproj",
                        arguments = Array.Empty<string>(),
                        workingDirectory = ".",
                        environment = new Dictionary<string, string>
                        {
                            ["SOURCE_NAME"] = "middle",
                        },
                        timeoutMilliseconds = 7000,
                        group = "middle",
                    },
                },
            },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static string CreateHarnessOnlyPlanJson()
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
                        id = "fake.harness",
                        displayName = "Fake harness",
                        kind = "executable_harness",
                        project = "tests/FakeHarness/FakeHarness.csproj",
                        arguments = Array.Empty<string>(),
                        workingDirectory = ".",
                        environment = new Dictionary<string, string>(),
                        timeoutMilliseconds = 5000,
                        group = "fake",
                    },
                },
            });
    }

    private static void ExpandWithScript(
        TestPlan plan,
        string repositoryRoot,
        params ToolProcessResult[] results)
    {
        TestPlanExpander.ExpandAsync(
                plan,
                repositoryRoot,
                "Debug",
                new ScriptedToolProcessRunner(results),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static ToolProcessResult CreateToolResult(
        int exitCode,
        string standardOutput,
        string standardError)
    {
        return new ToolProcessResult(
            exitCode,
            CaptureToolText(standardOutput),
            CaptureToolText(standardError));
    }

    private static CapturedProcessStream CaptureToolText(string value)
    {
        if (value.Length == 0)
        {
            return new CapturedProcessStream(Evidence: null, Content: null);
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(value), writable: false);
        return BoundedProcessStreamCapture.CaptureAsync(stream)
            .GetAwaiter()
            .GetResult();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Doccer.slnx")) &&
                File.Exists(Path.Combine(
                    directory.FullName,
                    "tests",
                    "Doccer.Tests",
                    "Doccer.Tests.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The Doccer repository root could not be located.");
    }

    private sealed class ScriptedToolProcessRunner : IToolProcessRunner
    {
        private readonly Queue<ToolProcessResult> _results;

        public ScriptedToolProcessRunner(params ToolProcessResult[] results)
        {
            _results = new Queue<ToolProcessResult>(results);
        }

        public List<ToolProcessRequest> Requests { get; } = new();

        public Task<ToolProcessResult> RunAsync(
            ToolProcessRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (!_results.TryDequeue(out var result))
            {
                throw new InvalidOperationException("No scripted tool-process result remains.");
            }

            return Task.FromResult(result);
        }
    }

    private const string ValidHarnessCatalogJson =
        """
        {
          "schemaVersion": 1,
          "protocol": "doccer-test-harness",
          "suiteId": "fake.contracts",
          "cases": [
            {
              "ordinal": 1,
              "id": "beta",
              "displayName": "Beta case",
              "concurrency": "exclusive"
            },
            {
              "ordinal": 2,
              "id": "alpha",
              "displayName": "Alpha case",
              "concurrency": "parallel"
            }
          ]
        }
        """;
}
