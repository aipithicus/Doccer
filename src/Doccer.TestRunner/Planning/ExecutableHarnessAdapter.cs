using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Doccer.TestRunner;

internal sealed class ExecutableHarnessExpansionException : Exception
{
    public ExecutableHarnessExpansionException(string message)
        : base(message)
    {
    }

    public ExecutableHarnessExpansionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class ExecutableHarnessAdapter
{
    private const string DotnetHost = "dotnet";
    private const int MaximumDiagnosticCharacters = 1024;
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EvaluationTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CatalogTimeout = TimeSpan.FromMinutes(1);
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly StringComparer ProjectPathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static async Task<IReadOnlyList<TestWorkItem>> ExpandAsync(
        IReadOnlyList<TestPlanEntry> sources,
        string repositoryRoot,
        string configuration,
        IToolProcessRunner processRunner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(processRunner);
        if (sources.Count == 0)
        {
            return Array.Empty<TestWorkItem>();
        }

        var root = RepositoryPaths.NormalizeRoot(repositoryRoot, nameof(repositoryRoot));
        using var workspace = HarnessExpansionWorkspace.Create(root);
        var projects = new Dictionary<string, HarnessProject>(ProjectPathComparer);
        var workItems = new List<TestWorkItem>();
        foreach (var source in sources.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (source.Kind != TestPlanEntryKind.ExecutableHarness || source.Project is null)
            {
                throw new ArgumentException(
                    "The executable-harness adapter accepts executable-harness sources only.",
                    nameof(sources));
            }

            if (!projects.TryGetValue(source.Project, out var project))
            {
                project = await DiscoverProjectAsync(
                        source,
                        root,
                        configuration,
                        workspace.Environment,
                        processRunner,
                        cancellationToken)
                    .ConfigureAwait(false);
                projects.Add(source.Project, project);
            }

            foreach (var testCase in project.Catalog.Cases)
            {
                workItems.Add(CreateWorkItem(source, project.TargetPath, testCase));
            }
        }

        return Array.AsReadOnly(workItems.ToArray());
    }

    internal static string CreateWorkItemId(string sourceId, string caseId) =>
        $"{EscapeIdentitySegment(sourceId)}/{EscapeIdentitySegment(caseId)}";

    internal static string GetCaseId(TestWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        if (workItem.Kind != TestWorkItemKind.HarnessCase ||
            workItem.Arguments.Count != 7 ||
            !string.Equals(workItem.Executable, DotnetHost, StringComparison.Ordinal) ||
            !string.Equals(workItem.Arguments[0], "exec", StringComparison.Ordinal) ||
            !string.Equals(workItem.Arguments[2], "run", StringComparison.Ordinal) ||
            !string.Equals(workItem.Arguments[3], "--case", StringComparison.Ordinal) ||
            !string.Equals(workItem.Arguments[5], "--format", StringComparison.Ordinal) ||
            !string.Equals(workItem.Arguments[6], "json", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A harness-case work item must retain the native exact-case argument contract.",
                nameof(workItem));
        }

        return workItem.Arguments[4];
    }

    private static async Task<HarnessProject> DiscoverProjectAsync(
        TestPlanEntry source,
        string repositoryRoot,
        string configuration,
        IReadOnlyDictionary<string, string> toolEnvironment,
        IToolProcessRunner processRunner,
        CancellationToken cancellationToken)
    {
        var projectPath = RepositoryPaths.ResolveContained(
            repositoryRoot,
            source.Project!,
            nameof(source.Project),
            allowRoot: false);
        try
        {
            RepositoryEntryGuard.RequireExistingWithoutReparsePoints(
                repositoryRoot,
                projectPath,
                directory: false,
                nameof(source.Project));
        }
        catch (Exception exception)
        {
            throw new ExecutableHarnessExpansionException(
                $"Executable-harness source '{source.Id}' project could not be validated: " +
                BoundDiagnostic(exception.Message),
                exception);
        }

        var build = await RunToolAsync(
                source.Id,
                "build",
                processRunner,
                new ToolProcessRequest(
                    DotnetHost,
                    Freeze(
                        "build",
                        projectPath,
                        "--configuration",
                        configuration,
                        "--nologo",
                        "--verbosity",
                        "quiet"),
                    repositoryRoot,
                    toolEnvironment,
                    BuildTimeout),
                cancellationToken)
            .ConfigureAwait(false);
        RequireSuccessfulExit(source.Id, "build", build);

        var evaluation = await RunToolAsync(
                source.Id,
                "target evaluation",
                processRunner,
                new ToolProcessRequest(
                    DotnetHost,
                    Freeze(
                        "msbuild",
                        projectPath,
                        "-nologo",
                        "-getProperty:TargetPath",
                        $"-property:Configuration={configuration}"),
                    repositoryRoot,
                    toolEnvironment,
                    EvaluationTimeout),
                cancellationToken)
            .ConfigureAwait(false);
        RequireSuccessfulExit(source.Id, "target evaluation", evaluation);
        RequireEmptyStandardError(source.Id, "target evaluation", evaluation.StandardError);
        var evaluatedTarget = RequireSingleOutputLine(
            source.Id,
            "target evaluation",
            evaluation.StandardOutput);
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new ExecutableHarnessExpansionException(
                $"Executable-harness source '{source.Id}' project has no parent directory.");
        string targetPath;
        try
        {
            var targetCandidate = Path.IsPathRooted(evaluatedTarget)
                ? evaluatedTarget
                : Path.GetFullPath(evaluatedTarget, projectDirectory);
            targetPath = RepositoryPaths.ResolveContained(
                repositoryRoot,
                targetCandidate,
                "evaluatedTargetPath",
                allowRoot: false);
            RepositoryEntryGuard.RequireExistingWithoutReparsePoints(
                repositoryRoot,
                targetPath,
                directory: false,
                "evaluatedTargetPath");
        }
        catch (Exception exception)
        {
            throw new ExecutableHarnessExpansionException(
                $"Executable-harness source '{source.Id}' evaluated target could not be " +
                $"validated: {BoundDiagnostic(exception.Message)}",
                exception);
        }

        if (!targetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExecutableHarnessExpansionException(
                $"Executable-harness source '{source.Id}' evaluated target must be a managed .dll.");
        }

        var discovery = await RunToolAsync(
                source.Id,
                "catalog discovery",
                processRunner,
                new ToolProcessRequest(
                    DotnetHost,
                    Freeze("exec", targetPath, "list", "--format", "json"),
                    repositoryRoot,
                    toolEnvironment,
                    CatalogTimeout),
                cancellationToken)
            .ConfigureAwait(false);
        RequireSuccessfulExit(source.Id, "catalog discovery", discovery);
        RequireEmptyStandardError(source.Id, "catalog discovery", discovery.StandardError);
        var catalogJson = RequireCompleteUtf8(
            source.Id,
            "catalog discovery",
            discovery.StandardOutput);
        try
        {
            return new HarnessProject(
                targetPath,
                ExecutableHarnessCatalogParser.Parse(catalogJson));
        }
        catch (ExecutableHarnessCatalogValidationException exception)
        {
            throw new ExecutableHarnessExpansionException(
                $"Executable-harness source '{source.Id}' returned an invalid catalog: " +
                BoundDiagnostic(exception.Message),
                exception);
        }
    }

    private static TestWorkItem CreateWorkItem(
        TestPlanEntry source,
        string targetPath,
        ExecutableHarnessCatalogCase testCase)
    {
        return new TestWorkItem(
            CreateWorkItemId(source.Id, testCase.Id),
            testCase.DisplayName,
            TestWorkItemKind.HarnessCase,
            source.Id,
            source.Project,
            DotnetHost,
            Freeze(
                "exec",
                targetPath,
                "run",
                "--case",
                testCase.Id,
                "--format",
                "json"),
            source.WorkingDirectory,
            source.Environment,
            source.TimeoutMilliseconds,
            source.Group,
            testCase.Concurrency);
    }

    private static async Task<ToolProcessResult> RunToolAsync(
        string sourceId,
        string operation,
        IToolProcessRunner processRunner,
        ToolProcessRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ExecutableHarnessExpansionException(
                $"Executable-harness source '{sourceId}' {operation} could not complete: " +
                BoundDiagnostic(exception.Message),
                exception);
        }
    }

    private static void RequireSuccessfulExit(
        string sourceId,
        string operation,
        ToolProcessResult result)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        var diagnostic = ProcessDiagnostic(result);
        throw new ExecutableHarnessExpansionException(
            $"Executable-harness source '{sourceId}' {operation} exited with code " +
            $"{result.ExitCode}.{(diagnostic.Length == 0 ? string.Empty : $" {diagnostic}")}");
    }

    private static void RequireEmptyStandardError(
        string sourceId,
        string operation,
        CapturedProcessStream standardError)
    {
        if (standardError.Evidence is null)
        {
            return;
        }

        throw new ExecutableHarnessExpansionException(
            $"Executable-harness source '{sourceId}' {operation} wrote unexpected stderr: " +
            BoundDiagnostic(DecodeForDiagnostic(standardError)));
    }

    private static string RequireSingleOutputLine(
        string sourceId,
        string operation,
        CapturedProcessStream standardOutput)
    {
        var value = RequireCompleteUtf8(sourceId, operation, standardOutput);
        using var reader = new StringReader(value);
        var line = reader.ReadLine();
        var extra = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(line) ||
            extra is not null ||
            !string.Equals(line, line.Trim(), StringComparison.Ordinal))
        {
            throw new ExecutableHarnessExpansionException(
                $"Executable-harness source '{sourceId}' {operation} must emit exactly one " +
                "trimmed output line.");
        }

        return line;
    }

    private static string RequireCompleteUtf8(
        string sourceId,
        string operation,
        CapturedProcessStream stream)
    {
        if (stream.Evidence?.Truncated == true)
        {
            throw new ExecutableHarnessExpansionException(
                $"Executable-harness source '{sourceId}' {operation} exceeded the " +
                $"{TestCaptureContract.MaximumRetainedBytesPerStream}-byte capture limit.");
        }

        if (stream.Content is null)
        {
            return string.Empty;
        }

        try
        {
            return StrictUtf8.GetString(stream.Content);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ExecutableHarnessExpansionException(
                $"Executable-harness source '{sourceId}' {operation} did not emit valid UTF-8.",
                exception);
        }
    }

    private static string ProcessDiagnostic(ToolProcessResult result)
    {
        var stream = result.StandardError.Evidence is not null
            ? result.StandardError
            : result.StandardOutput;
        return stream.Evidence is null
            ? string.Empty
            : BoundDiagnostic(DecodeForDiagnostic(stream));
    }

    private static string DecodeForDiagnostic(CapturedProcessStream stream)
    {
        return stream.Content is null
            ? string.Empty
            : Encoding.UTF8.GetString(stream.Content);
    }

    private static string BoundDiagnostic(string? message)
    {
        var normalized = string.IsNullOrWhiteSpace(message)
            ? "No diagnostic message was supplied."
            : string.Join(
                ' ',
                message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= MaximumDiagnosticCharacters
            ? normalized
            : normalized[..(MaximumDiagnosticCharacters - 3)] + "...";
    }

    private static IReadOnlyList<string> Freeze(params string[] values) =>
        Array.AsReadOnly(values);

    private static string EscapeIdentitySegment(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    private sealed record HarnessProject(
        string TargetPath,
        ExecutableHarnessCatalog Catalog);
}
