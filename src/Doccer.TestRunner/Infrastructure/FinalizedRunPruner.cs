using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Doccer.TestRunner;

internal static class FinalizedRunPruner
{
    internal const long MaximumSummaryBytes = 64L * 1024 * 1024;

    private const int RunStampLength = 16;
    private const int ShortRunIdLength = 16;
    private static readonly TimeSpan RetentionLockTimeout = TimeSpan.FromSeconds(5);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static int Prune(
        TestArtifactLayout currentArtifacts,
        TestRunSummary currentSummary)
    {
        ArgumentNullException.ThrowIfNull(currentArtifacts);
        ArgumentNullException.ThrowIfNull(currentSummary);

        var repositoryRoot = RepositoryPaths.NormalizeRoot(
            currentArtifacts.RepositoryRoot,
            nameof(currentArtifacts));
        var generatedRoot = RepositoryPaths.ResolveContained(
            repositoryRoot,
            currentArtifacts.GeneratedRoot,
            nameof(currentArtifacts),
            allowRoot: false);
        var expectedGeneratedRoot = Path.Combine(repositoryRoot, "build", "test-runs");
        if (!RepositoryPaths.Same(generatedRoot, expectedGeneratedRoot))
        {
            throw new ArgumentException(
                "Finalized-run retention is confined to build/test-runs.",
                nameof(currentArtifacts));
        }

        RepositoryEntryGuard.RequireExistingWithoutReparsePoints(
            repositoryRoot,
            generatedRoot,
            directory: true,
            nameof(currentArtifacts));

        using var retentionLock = new Mutex(
            initiallyOwned: false,
            name: CreateMutexName(generatedRoot));
        var lockTaken = false;
        try
        {
            try
            {
                lockTaken = retentionLock.WaitOne(RetentionLockTimeout);
            }
            catch (AbandonedMutexException)
            {
                lockTaken = true;
            }

            if (!lockTaken)
            {
                return 0;
            }

            return PruneUnderLock(
                repositoryRoot,
                generatedRoot,
                currentArtifacts.RunDirectory,
                currentSummary);
        }
        finally
        {
            if (lockTaken)
            {
                retentionLock.ReleaseMutex();
            }
        }
    }

    private static int PruneUnderLock(
        string repositoryRoot,
        string generatedRoot,
        string currentRunDirectory,
        TestRunSummary currentSummary)
    {
        var current = CreateCurrentCandidate(
            repositoryRoot,
            generatedRoot,
            currentRunDirectory,
            currentSummary);
        var candidates = new List<FinalizedRunCandidate> { current };
        foreach (var entry in Directory.EnumerateFileSystemEntries(
                     generatedRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            if (RepositoryPaths.Same(entry, current.Path) ||
                !TryReadCandidate(repositoryRoot, generatedRoot, entry, out var candidate))
            {
                continue;
            }

            candidates.Add(candidate);
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.CompletedAtUtc)
            .ThenByDescending(candidate => candidate.DirectoryName, StringComparer.Ordinal)
            .ToArray();
        var retained = ordered
            .Take(TestRunRetentionContract.MaximumFinalizedRunCount)
            .Select(candidate => candidate.Path)
            .ToHashSet(RepositoryPathComparer.Instance);

        var pruned = 0;
        foreach (var candidate in ordered
                     .Skip(TestRunRetentionContract.MaximumFinalizedRunCount)
                     .OrderBy(candidate => candidate.CompletedAtUtc)
                     .ThenBy(candidate => candidate.DirectoryName, StringComparer.Ordinal))
        {
            if (retained.Contains(candidate.Path) ||
                candidate.IsCurrent ||
                IsClaimActive(generatedRoot, candidate.ShortRunId))
            {
                continue;
            }

            if (!TryReadCandidate(repositoryRoot, generatedRoot, candidate.Path, out var verified) ||
                !string.Equals(verified.RunId, candidate.RunId, StringComparison.Ordinal) ||
                verified.CompletedAtUtc != candidate.CompletedAtUtc ||
                IsClaimActive(generatedRoot, candidate.ShortRunId))
            {
                continue;
            }

            RepositoryTree.DeleteContained(generatedRoot, candidate.Path);
            if (Directory.Exists(candidate.Path) || File.Exists(candidate.Path))
            {
                throw new IOException(
                    $"Finalized test run '{candidate.DirectoryName}' could not be pruned.");
            }

            pruned++;
        }

        return pruned;
    }

    private static FinalizedRunCandidate CreateCurrentCandidate(
        string repositoryRoot,
        string generatedRoot,
        string currentRunDirectory,
        TestRunSummary summary)
    {
        var resolved = RepositoryPaths.ResolveContained(
            generatedRoot,
            currentRunDirectory,
            nameof(currentRunDirectory),
            allowRoot: false);
        var parent = Path.GetDirectoryName(resolved);
        if (parent is null || !RepositoryPaths.Same(parent, generatedRoot))
        {
            throw new InvalidOperationException(
                "The current run must be a direct child of build/test-runs.");
        }

        if (!TryParseRunDirectoryName(
                Path.GetFileName(resolved),
                out var requestedAtUtc,
                out var shortRunId) ||
            !ValidateSummary(repositoryRoot, resolved, requestedAtUtc, shortRunId, summary))
        {
            throw new InvalidOperationException(
                "The current finalized run does not satisfy its retention identity contract.");
        }

        return new FinalizedRunCandidate(
            resolved,
            Path.GetFileName(resolved),
            summary.RunId,
            shortRunId,
            summary.CompletedAtUtc,
            IsCurrent: true);
    }

    private static bool TryReadCandidate(
        string repositoryRoot,
        string generatedRoot,
        string path,
        out FinalizedRunCandidate candidate)
    {
        candidate = null!;
        try
        {
            var resolved = RepositoryPaths.ResolveContained(
                generatedRoot,
                path,
                nameof(path),
                allowRoot: false);
            var parent = Path.GetDirectoryName(resolved);
            if (parent is null || !RepositoryPaths.Same(parent, generatedRoot))
            {
                return false;
            }

            var attributes = File.GetAttributes(resolved);
            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            var directoryName = Path.GetFileName(resolved);
            if (!TryParseRunDirectoryName(
                    directoryName,
                    out var requestedAtUtc,
                    out var shortRunId))
            {
                return false;
            }

            var planPath = Path.Combine(resolved, "plan.json");
            var eventsPath = Path.Combine(resolved, "events.jsonl");
            var summaryPath = Path.Combine(resolved, "summary.json");
            if (!IsRegularNonemptyFile(planPath) ||
                !IsRegularNonemptyFile(eventsPath) ||
                !IsRegularNonemptyFile(summaryPath))
            {
                return false;
            }

            var summaryInfo = new FileInfo(summaryPath);
            if (summaryInfo.Length > MaximumSummaryBytes)
            {
                return false;
            }

            var json = ReadStrictUtf8(summaryPath);
            using (var syntax = JsonDocument.Parse(
                       json,
                       new JsonDocumentOptions
                       {
                           AllowTrailingCommas = false,
                           CommentHandling = JsonCommentHandling.Disallow,
                           MaxDepth = 64,
                       }))
            {
                RejectDuplicateProperties(syntax.RootElement, "$");
            }

            var summary = TestRunnerJson.Deserialize<TestRunSummary>(json);
            if (summary is null ||
                !ValidateSummary(
                    repositoryRoot,
                    resolved,
                    requestedAtUtc,
                    shortRunId,
                    summary))
            {
                return false;
            }

            candidate = new FinalizedRunCandidate(
                resolved,
                directoryName,
                summary.RunId,
                shortRunId,
                summary.CompletedAtUtc,
                IsCurrent: false);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            DecoderFallbackException or
            JsonException or
            ArgumentException or
            InvalidOperationException or
            NotSupportedException)
        {
            return false;
        }
    }

    private static bool ValidateSummary(
        string repositoryRoot,
        string runDirectory,
        DateTimeOffset directoryRequestedAtUtc,
        string shortRunId,
        TestRunSummary summary)
    {
        if (summary.SchemaVersion != TestRunnerProtocol.SchemaVersion ||
            !string.Equals(summary.Protocol, TestRunnerProtocol.Summary, StringComparison.Ordinal) ||
            !IsLowerHex(summary.RunId, expectedLength: 32) ||
            !summary.RunId.StartsWith(shortRunId, StringComparison.Ordinal) ||
            summary.Counts is null ||
            summary.Results is null ||
            summary.Details is null ||
            summary.Results.Count < 1 ||
            summary.Results.Count > TestArtifactLayout.MaximumWorkItemCount)
        {
            return false;
        }

        TestRunPlanSnapshot.RequireUtc(summary.RequestedAtUtc, nameof(summary));
        TestRunPlanSnapshot.RequireUtc(summary.StartedAtUtc, nameof(summary));
        TestRunPlanSnapshot.RequireUtc(summary.CompletedAtUtc, nameof(summary));
        if (summary.RequestedAtUtc > summary.StartedAtUtc ||
            summary.StartedAtUtc > summary.CompletedAtUtc ||
            summary.RequestedAtUtc.ToString(
                "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture) !=
            directoryRequestedAtUtc.ToString(
                "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture))
        {
            return false;
        }

        var resultIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in summary.Results)
        {
            if (result is null)
            {
                return false;
            }

            result.Validate();
            if (!string.Equals(result.RunId, summary.RunId, StringComparison.Ordinal) ||
                !resultIds.Add(result.WorkItemId) ||
                result.StartedAtUtc is { } started && started < summary.StartedAtUtc ||
                result.CompletedAtUtc is { } completed && completed > summary.CompletedAtUtc)
            {
                return false;
            }
        }

        var expectedCounts = new TestStatusCounts(
            summary.Results.Count(result => result.Status == TestExecutionStatus.Passed),
            summary.Results.Count(result => result.Status == TestExecutionStatus.Failed),
            summary.Results.Count(result => result.Status == TestExecutionStatus.Canceled),
            summary.Results.Count(result => result.Status == TestExecutionStatus.TimedOut),
            summary.Results.Count(result => result.Status == TestExecutionStatus.NotStarted),
            summary.Results.Count(result => result.Status == TestExecutionStatus.InfrastructureError));
        if (summary.Counts != expectedCounts ||
            summary.ExitCode != (int)TestRunSummary.ResolveExitCode(summary.Results))
        {
            return false;
        }

        var expectedDetails = summary.Results
            .Where(TestEvidenceMaterializationPolicy.RequiresCaseDetails)
            .Select(result => result.WorkItemId)
            .ToHashSet(StringComparer.Ordinal);
        var detailIds = new HashSet<string>(StringComparer.Ordinal);
        var detailPaths = new HashSet<string>(RepositoryPathComparer.Instance);
        var caseRoot = Path.Combine(runDirectory, "c");
        foreach (var detail in summary.Details)
        {
            if (detail is null ||
                string.IsNullOrWhiteSpace(detail.WorkItemId) ||
                string.IsNullOrWhiteSpace(detail.ResultPath) ||
                !expectedDetails.Contains(detail.WorkItemId) ||
                !detailIds.Add(detail.WorkItemId))
            {
                return false;
            }

            var resolvedDetail = RepositoryPaths.ResolveContained(
                repositoryRoot,
                detail.ResultPath,
                nameof(detail.ResultPath),
                allowRoot: false);
            if (!RepositoryPaths.Contains(caseRoot, resolvedDetail, allowRoot: false) ||
                !string.Equals(
                    Path.GetFileName(resolvedDetail),
                    "result.json",
                    StringComparison.Ordinal) ||
                !detailPaths.Add(resolvedDetail))
            {
                return false;
            }

            RepositoryEntryGuard.RequireExistingWithoutReparsePoints(
                repositoryRoot,
                resolvedDetail,
                directory: false,
                nameof(detail.ResultPath));
            if (new FileInfo(resolvedDetail).Length == 0)
            {
                return false;
            }
        }

        return expectedDetails.SetEquals(detailIds);
    }

    private static bool TryParseRunDirectoryName(
        string name,
        out DateTimeOffset requestedAtUtc,
        out string shortRunId)
    {
        requestedAtUtc = default;
        shortRunId = string.Empty;
        if (name.Length != RunStampLength + 1 + ShortRunIdLength ||
            name[RunStampLength] != '-')
        {
            return false;
        }

        var stamp = name[..RunStampLength];
        shortRunId = name[(RunStampLength + 1)..];
        return IsLowerHex(shortRunId, ShortRunIdLength) &&
            DateTimeOffset.TryParseExact(
                stamp,
                "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out requestedAtUtc);
    }

    private static bool IsClaimActive(string generatedRoot, string shortRunId)
    {
        var claimPath = Path.Combine(generatedRoot, $".{shortRunId}.claim");
        if (!File.Exists(claimPath) && !Directory.Exists(claimPath))
        {
            return false;
        }

        try
        {
            var attributes = File.GetAttributes(claimPath);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                return true;
            }

            using var claim = new FileStream(
                claimPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsRegularNonemptyFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var attributes = File.GetAttributes(path);
        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0 &&
            new FileInfo(path).Length > 0;
    }

    private static string ReadStrictUtf8(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            StrictUtf8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: false);
        return reader.ReadToEnd();
    }

    private static void RejectDuplicateProperties(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException(
                        $"Duplicate JSON property '{property.Name}' at {path}.");
                }

                RejectDuplicateProperties(property.Value, $"{path}.{property.Name}");
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            RejectDuplicateProperties(item, $"{path}[{index++}]");
        }
    }

    private static bool IsLowerHex(string value, int expectedLength) =>
        value.Length == expectedLength &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string CreateMutexName(string generatedRoot)
    {
        var identity = OperatingSystem.IsWindows()
            ? generatedRoot.ToUpperInvariant()
            : generatedRoot;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"Doccer.TestRunner.Retention.{Convert.ToHexString(digest.AsSpan(0, 16))}";
    }

    private sealed record FinalizedRunCandidate(
        string Path,
        string DirectoryName,
        string RunId,
        string ShortRunId,
        DateTimeOffset CompletedAtUtc,
        bool IsCurrent);

    private sealed class RepositoryPathComparer : IEqualityComparer<string>
    {
        public static RepositoryPathComparer Instance { get; } = new();

        public bool Equals(string? left, string? right) =>
            left is not null && right is not null && RepositoryPaths.Same(left, right);

        public int GetHashCode(string value)
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
            return OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase.GetHashCode(normalized)
                : StringComparer.Ordinal.GetHashCode(normalized);
        }
    }
}
