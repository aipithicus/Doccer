using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Doccer.TestRunner;

internal sealed record TestArtifactLayout(
    string RepositoryRoot,
    string GeneratedRoot,
    string RunDirectory,
    string PlanPath,
    string EventsPath,
    string SummaryPath,
    IReadOnlyList<TestCaseArtifactLayout> Cases)
{
    public const int MaximumWorkItemCount = 9999;
    public const int MaximumManagedPathLength = 220;
    public const int MaximumRunDirectoryNameLength = 64;

    public static TestArtifactLayout Create(
        string repositoryRoot,
        string runDirectory,
        IReadOnlyList<TestWorkItem> workItems)
    {
        ArgumentNullException.ThrowIfNull(workItems);

        var root = RepositoryPaths.NormalizeRoot(repositoryRoot, nameof(repositoryRoot));
        var generatedRoot = Path.Combine(root, "build", "test-runs");
        var resolvedRunDirectory = RepositoryPaths.ResolveContained(
            generatedRoot,
            Path.IsPathRooted(runDirectory)
                ? runDirectory
                : Path.GetFullPath(runDirectory, root),
            nameof(runDirectory),
            allowRoot: false);
        var runParent = Path.GetDirectoryName(resolvedRunDirectory);
        if (runParent is null || !RepositoryPaths.Same(generatedRoot, runParent))
        {
            throw new ArgumentException(
                "A run directory must be a direct child of build/test-runs.",
                nameof(runDirectory));
        }

        var runDirectoryName = Path.GetFileName(resolvedRunDirectory);
        if (runDirectoryName.Length > MaximumRunDirectoryNameLength)
        {
            throw new ArgumentException(
                $"A run-directory name cannot exceed {MaximumRunDirectoryNameLength} characters.",
                nameof(runDirectory));
        }

        RequireManagedPath(resolvedRunDirectory, nameof(runDirectory));

        if (workItems.Count == 0)
        {
            throw new ArgumentException("At least one work item is required.", nameof(workItems));
        }

        if (workItems.Count > MaximumWorkItemCount)
        {
            throw new ArgumentException(
                $"A run cannot contain more than {MaximumWorkItemCount} work items.",
                nameof(workItems));
        }

        var ordered = workItems.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cases = new TestCaseArtifactLayout[ordered.Length];
        for (var index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];
            if (!ids.Add(item.Id))
            {
                throw new ArgumentException(
                    $"Duplicate work-item ID '{item.Id}'.",
                    nameof(workItems));
            }

            var ordinal = index + 1;
            var directoryName = $"{ordinal:D4}-{ShortIdentity(item.Id)}";
            var caseDirectory = RepositoryPaths.ResolveContained(
                resolvedRunDirectory,
                Path.Combine(resolvedRunDirectory, "c", directoryName),
                nameof(workItems),
                allowRoot: false);

            var caseLayout = new TestCaseArtifactLayout(
                item.Id,
                ordinal,
                directoryName,
                caseDirectory,
                Path.Combine(caseDirectory, "out.log"),
                Path.Combine(caseDirectory, "err.log"),
                Path.Combine(caseDirectory, "result.json"),
                Path.Combine(caseDirectory, "a"),
                Path.Combine(caseDirectory, "tmp"));
            RequireManagedPaths(caseLayout);
            cases[index] = caseLayout;
        }

        var layout = new TestArtifactLayout(
            root,
            generatedRoot,
            resolvedRunDirectory,
            Path.Combine(resolvedRunDirectory, "plan.json"),
            Path.Combine(resolvedRunDirectory, "events.jsonl"),
            Path.Combine(resolvedRunDirectory, "summary.json"),
            Array.AsReadOnly(cases));
        RequireManagedPath(layout.PlanPath, nameof(runDirectory));
        RequireManagedPath(layout.EventsPath, nameof(runDirectory));
        RequireManagedPath(layout.SummaryPath, nameof(runDirectory));
        return layout;
    }

    public static string CreateRunDirectoryName(DateTimeOffset requestedAtUtc, Guid runId)
    {
        if (requestedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Run timestamps must use UTC.", nameof(requestedAtUtc));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A nonempty run ID is required.", nameof(runId));
        }

        var shortRunId = runId.ToString("N")[..16];
        return $"{requestedAtUtc:yyyyMMdd'T'HHmmss'Z'}-{shortRunId}";
    }

    private static void RequireManagedPaths(TestCaseArtifactLayout layout)
    {
        RequireManagedPath(layout.CaseDirectory, nameof(layout));
        RequireManagedPath(layout.StandardOutputPath, nameof(layout));
        RequireManagedPath(layout.StandardErrorPath, nameof(layout));
        RequireManagedPath(layout.ResultPath, nameof(layout));
        RequireManagedPath(layout.ArtifactDirectory, nameof(layout));
        RequireManagedPath(layout.TempDirectory, nameof(layout));
    }

    private static void RequireManagedPath(string path, string parameterName)
    {
        if (path.Length > MaximumManagedPathLength)
        {
            throw new ArgumentException(
                $"Runner-managed paths cannot exceed {MaximumManagedPathLength} characters: '{path}'.",
                parameterName);
        }
    }

    private static string ShortIdentity(string id)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return Convert.ToHexString(digest.AsSpan(0, 6)).ToLowerInvariant();
    }
}

internal sealed record TestCaseArtifactLayout(
    string WorkItemId,
    int Ordinal,
    string DirectoryName,
    string CaseDirectory,
    string StandardOutputPath,
    string StandardErrorPath,
    string ResultPath,
    string ArtifactDirectory,
    string TempDirectory);
