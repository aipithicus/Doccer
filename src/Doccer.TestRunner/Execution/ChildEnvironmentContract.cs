using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Doccer.TestRunner;

internal static class ChildEnvironmentContract
{
    public const string RunId = "DOCCER_TEST_RUN_ID";
    public const string RunDirectory = "DOCCER_TEST_RUN_DIRECTORY";
    public const string CaseId = "DOCCER_TEST_CASE_ID";
    public const string CaseArtifactDirectory = "DOCCER_TEST_CASE_ARTIFACT_DIRECTORY";
    public const string CaseTempDirectory = "DOCCER_TEST_CASE_TEMP_DIRECTORY";
    public const string RunPlan = "DOCCER_TEST_RUN_PLAN";
    public const string ArtifactMaximumComponents = "DOCCER_TEST_ARTIFACT_MAX_COMPONENTS";
    public const string ArtifactMaximumComponentCharacters =
        "DOCCER_TEST_ARTIFACT_MAX_COMPONENT_CHARS";
    public const string ArtifactMaximumRelativeCharacters =
        "DOCCER_TEST_ARTIFACT_MAX_RELATIVE_CHARS";
    public const string ArtifactMaximumFiles = "DOCCER_TEST_ARTIFACT_MAX_FILES";
    public const string ArtifactMaximumFileBytes = "DOCCER_TEST_ARTIFACT_MAX_FILE_BYTES";
    public const string ArtifactMaximumTotalBytes = "DOCCER_TEST_ARTIFACT_MAX_TOTAL_BYTES";
    public const string StreamRetainedHeadBytes = "DOCCER_TEST_STREAM_RETAINED_HEAD_BYTES";
    public const string StreamRetainedTailBytes = "DOCCER_TEST_STREAM_RETAINED_TAIL_BYTES";

    private static readonly HashSet<string> StandardTempNames = new(
        new[] { "TMPDIR", "TMP", "TEMP" },
        StringComparer.OrdinalIgnoreCase);

    public static bool IsReserved(string name) =>
        name.StartsWith("DOCCER_TEST_", StringComparison.OrdinalIgnoreCase) ||
        StandardTempNames.Contains(name);

    public static IReadOnlyDictionary<string, string> CreateAdditions(
        IReadOnlyDictionary<string, string> planEnvironment,
        string runId,
        TestArtifactLayout runLayout,
        TestCaseArtifactLayout caseLayout)
    {
        ArgumentNullException.ThrowIfNull(planEnvironment);
        ArgumentNullException.ThrowIfNull(runLayout);
        ArgumentNullException.ThrowIfNull(caseLayout);

        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("A run ID is required.", nameof(runId));
        }

        if (!RepositoryPaths.Contains(runLayout.RunDirectory, caseLayout.CaseDirectory, allowRoot: false) ||
            !RepositoryPaths.Contains(caseLayout.CaseDirectory, caseLayout.ArtifactDirectory, allowRoot: false) ||
            !RepositoryPaths.Contains(caseLayout.CaseDirectory, caseLayout.TempDirectory, allowRoot: false) ||
            !runLayout.Cases.Contains(caseLayout))
        {
            throw new ArgumentException("Case paths must remain beneath the run directory.", nameof(caseLayout));
        }

        var additions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in planEnvironment)
        {
            if (IsReserved(pair.Key))
            {
                throw new ArgumentException(
                    $"Plan environment cannot override reserved name '{pair.Key}'.",
                    nameof(planEnvironment));
            }

            if (!names.Add(pair.Key))
            {
                throw new ArgumentException(
                    $"Plan environment contains names that differ only by case: '{pair.Key}'.",
                    nameof(planEnvironment));
            }

            additions.Add(pair.Key, pair.Value);
        }

        additions.Add(RunId, runId);
        additions.Add(RunDirectory, runLayout.RunDirectory);
        additions.Add(CaseId, caseLayout.WorkItemId);
        additions.Add(CaseArtifactDirectory, caseLayout.ArtifactDirectory);
        additions.Add(CaseTempDirectory, caseLayout.TempDirectory);
        additions.Add(RunPlan, runLayout.PlanPath);
        additions.Add(
            ArtifactMaximumComponents,
            ArtifactPathContract.MaximumComponentCount.ToString(CultureInfo.InvariantCulture));
        additions.Add(
            ArtifactMaximumComponentCharacters,
            ArtifactPathContract.MaximumComponentLength.ToString(CultureInfo.InvariantCulture));
        additions.Add(
            ArtifactMaximumRelativeCharacters,
            ArtifactPathContract.MaximumRelativePathLength.ToString(CultureInfo.InvariantCulture));
        additions.Add(
            ArtifactMaximumFiles,
            TestCaptureContract.MaximumArtifactFileCount.ToString(CultureInfo.InvariantCulture));
        additions.Add(
            ArtifactMaximumFileBytes,
            TestCaptureContract.MaximumArtifactFileBytes.ToString(CultureInfo.InvariantCulture));
        additions.Add(
            ArtifactMaximumTotalBytes,
            TestCaptureContract.MaximumArtifactTotalBytes.ToString(CultureInfo.InvariantCulture));
        additions.Add(
            StreamRetainedHeadBytes,
            TestCaptureContract.RetainedHeadBytesPerTruncatedStream.ToString(
                CultureInfo.InvariantCulture));
        additions.Add(
            StreamRetainedTailBytes,
            TestCaptureContract.RetainedTailBytesPerTruncatedStream.ToString(
                CultureInfo.InvariantCulture));
        additions.Add("TMPDIR", caseLayout.TempDirectory);
        additions.Add("TMP", caseLayout.TempDirectory);
        additions.Add("TEMP", caseLayout.TempDirectory);

        return new ReadOnlyDictionary<string, string>(additions);
    }
}
