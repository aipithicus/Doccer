using System;
using System.Collections.Generic;
using System.IO;

namespace Doccer.TestRunner.Tests;

internal static partial class Program
{
    private static void ArtifactLayoutIsContainedAndCollisionSafe()
    {
        var items = CommandPlanExpander.Expand(
            TestPlanParser.Parse(ValidPlanJson, ContractRepositoryRoot)).Commands;
        var runRelativePath = "build/test-runs/20260903T120000Z__contract";
        var layout = TestArtifactLayout.Create(
            ContractRepositoryRoot,
            runRelativePath,
            items);

        Equal(
            Path.Combine(ContractRepositoryRoot, "build", "test-runs"),
            layout.GeneratedRoot,
            "generated root");
        Equal(2, layout.Cases.Count, "case layout count");
        Equal("a.command", layout.Cases[0].WorkItemId, "artifact stable ordering");
        Equal("z.command", layout.Cases[1].WorkItemId, "artifact second ordering");
        True(layout.Cases[0].DirectoryName.StartsWith("0001-", StringComparison.Ordinal), "first compact identity");
        True(layout.Cases[1].DirectoryName.StartsWith("0002-", StringComparison.Ordinal), "second compact identity");
        True(
            !StringComparer.Ordinal.Equals(
                layout.Cases[0].DirectoryName,
                layout.Cases[1].DirectoryName),
            "case identities retain distinct hashes");
        Equal(17, layout.Cases[0].DirectoryName.Length, "case directory name length");
        Equal(
            "c",
            Path.GetFileName(Path.GetDirectoryName(layout.Cases[0].CaseDirectory)),
            "compact case root");
        Equal("out.log", Path.GetFileName(layout.Cases[0].StandardOutputPath), "compact stdout name");
        Equal("err.log", Path.GetFileName(layout.Cases[0].StandardErrorPath), "compact stderr name");
        Equal("a", Path.GetFileName(layout.Cases[0].ArtifactDirectory), "compact artifact directory");
        True(
            RepositoryPaths.Contains(layout.RunDirectory, layout.Cases[0].ArtifactDirectory, false),
            "artifact directory containment");
        True(
            RepositoryPaths.Contains(layout.RunDirectory, layout.Cases[0].TempDirectory, false),
            "temp directory containment");
        Equal(
            Path.Combine(layout.RunDirectory, "plan.json"),
            layout.PlanPath,
            "run plan path");

        var renamedItem = items[0] with { DisplayName = new string('x', 200) };
        var renamedLayout = TestArtifactLayout.Create(
            ContractRepositoryRoot,
            "build/test-runs/long-name",
            new[] { renamedItem });
        Equal(17, renamedLayout.Cases[0].DirectoryName.Length, "display name is absent from physical path");

        Throws<ArgumentException>(
            () => TestArtifactLayout.Create(
                ContractRepositoryRoot,
                "../outside",
                items),
            "run-directory escape rejection");
        Throws<ArgumentException>(
            () => TestArtifactLayout.Create(
                ContractRepositoryRoot,
                "build/test-runs",
                items),
            "generated root cannot itself be a run");
        Throws<ArgumentException>(
            () => TestArtifactLayout.Create(
                ContractRepositoryRoot,
                "build/test-runs/nested/run",
                items),
            "run directory must be a direct child");
        Throws<ArgumentException>(
            () => TestArtifactLayout.Create(
                ContractRepositoryRoot,
                $"build/test-runs/{new string('r', 65)}",
                items),
            "run directory name length rejection");
        var excessiveItems = new TestWorkItem[TestArtifactLayout.MaximumWorkItemCount + 1];
        Throws<ArgumentException>(
            () => TestArtifactLayout.Create(
                ContractRepositoryRoot,
                "build/test-runs/too-many",
                excessiveItems),
            "artifact work-item count rejection");
        var excessiveRoot = Path.Combine(ContractRepositoryRoot, new string('r', 180));
        Throws<ArgumentException>(
            () => TestArtifactLayout.Create(
                excessiveRoot,
                Path.Combine(excessiveRoot, "build", "test-runs", "run"),
                items),
            "runner-managed full path rejection");
        Throws<ArgumentException>(
            () => TestArtifactLayout.Create(
                ContractRepositoryRoot,
                "build/test-runs/duplicate",
                new[] { items[0], items[0] }),
            "duplicate artifact identity rejection");

        var runId = Guid.Parse("75f0f79d-d895-441c-a101-f3d65b024a2e");
        Equal(
            "20260903T120000Z-75f0f79dd895441c",
            TestArtifactLayout.CreateRunDirectoryName(
                new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero),
                runId),
            "canonical run directory name");
        Throws<ArgumentException>(
            () => TestArtifactLayout.CreateRunDirectoryName(
                new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.FromHours(1)),
                runId),
            "non-UTC run directory timestamp rejection");
        Throws<ArgumentException>(
            () => RepositoryPaths.NormalizeRoot("relative-root", "root"),
            "relative repository root rejection");

        Equal(
            Path.Combine(layout.Cases[0].ArtifactDirectory, "nested", "evidence.txt"),
            ArtifactPathContract.ResolveFile(
                layout.Cases[0].ArtifactDirectory,
                "nested/evidence.txt"),
            "portable artifact path resolution");
        Throws<ArgumentException>(
            () => ArtifactPathContract.ResolveFile(
                layout.Cases[0].ArtifactDirectory,
                "../escape.txt"),
            "artifact parent traversal rejection");
        Throws<ArgumentException>(
            () => ArtifactPathContract.ResolveFile(
                layout.Cases[0].ArtifactDirectory,
                "one/two/three/evidence.txt"),
            "artifact depth rejection");
        Throws<ArgumentException>(
            () => ArtifactPathContract.ResolveFile(
                layout.Cases[0].ArtifactDirectory,
                $"{new string('a', 65)}.txt"),
            "artifact component length rejection");
        Throws<ArgumentException>(
            () => ArtifactPathContract.ResolveFile(
                layout.Cases[0].ArtifactDirectory,
                $"{new string('a', 60)}/{new string('b', 60)}"),
            "artifact relative length rejection");
        Throws<ArgumentException>(
            () => ArtifactPathContract.ResolveFile(
                layout.Cases[0].ArtifactDirectory,
                "CON.txt"),
            "portable reserved-name rejection");
        Throws<ArgumentException>(
            () => ArtifactPathContract.ResolveFile(
                layout.Cases[0].ArtifactDirectory,
                "bad?.txt"),
            "portable invalid-character rejection");
        Throws<ArgumentException>(
            () => ArtifactPathContract.ResolveFile(
                layout.Cases[0].ArtifactDirectory,
                "double//name.txt"),
            "artifact empty-component rejection");
    }

    private static void ChildEnvironmentIsIsolatedAndReserved()
    {
        var items = CommandPlanExpander.Expand(
            TestPlanParser.Parse(ValidPlanJson, ContractRepositoryRoot)).Commands;
        var layout = TestArtifactLayout.Create(
            ContractRepositoryRoot,
            "build/test-runs/environment",
            items);
        var item = items[1];
        var caseLayout = layout.Cases[1];

        var environment = ChildEnvironmentContract.CreateAdditions(
            item.Environment,
            "run-identity",
            layout,
            caseLayout);

        Equal("one", environment["ALPHA"], "plan environment retained");
        Equal("run-identity", environment[ChildEnvironmentContract.RunId], "run ID environment");
        Equal(item.Id, environment[ChildEnvironmentContract.CaseId], "case ID environment");
        Equal(
            caseLayout.ArtifactDirectory,
            environment[ChildEnvironmentContract.CaseArtifactDirectory],
            "artifact environment");
        Equal(caseLayout.TempDirectory, environment["TMPDIR"], "TMPDIR isolation");
        Equal(caseLayout.TempDirectory, environment["TMP"], "TMP isolation");
        Equal(caseLayout.TempDirectory, environment["TEMP"], "TEMP isolation");
        Equal("3", environment[ChildEnvironmentContract.ArtifactMaximumComponents], "artifact depth environment");
        Equal(
            "64",
            environment[ChildEnvironmentContract.ArtifactMaximumComponentCharacters],
            "artifact component environment");
        Equal(
            "120",
            environment[ChildEnvironmentContract.ArtifactMaximumRelativeCharacters],
            "artifact relative path environment");
        Equal("8", environment[ChildEnvironmentContract.ArtifactMaximumFiles], "artifact count environment");
        Equal(
            "4194304",
            environment[ChildEnvironmentContract.ArtifactMaximumFileBytes],
            "artifact file bytes environment");
        Equal(
            "8388608",
            environment[ChildEnvironmentContract.ArtifactMaximumTotalBytes],
            "artifact total bytes environment");
        Equal(
            "65536",
            environment[ChildEnvironmentContract.StreamRetainedHeadBytes],
            "stream head environment");
        Equal(
            "196608",
            environment[ChildEnvironmentContract.StreamRetainedTailBytes],
            "stream tail environment");

        Throws<ArgumentException>(
            () => ChildEnvironmentContract.CreateAdditions(
                new Dictionary<string, string>
                {
                    ["DOCCER_TEST_CASE_ID"] = "override",
                },
                "run-identity",
                layout,
                caseLayout),
            "reserved environment override rejection");
        Throws<ArgumentException>(
            () => ChildEnvironmentContract.CreateAdditions(
                item.Environment,
                "run-identity",
                layout,
                caseLayout with { WorkItemId = "forged.item" }),
            "foreign case layout rejection");
        Throws<NotSupportedException>(
            () => ((IDictionary<string, string>)environment).Add("LATE", "mutation"),
            "child environment is frozen");
    }
}
