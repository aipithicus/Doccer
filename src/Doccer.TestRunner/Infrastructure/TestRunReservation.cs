using System;
using System.Collections.Generic;
using System.IO;

namespace Doccer.TestRunner;

internal sealed class TestRunReservation : IDisposable
{
    private readonly FileStream _claim;
    private bool _disposed;

    private TestRunReservation(
        Guid runIdentity,
        TestArtifactLayout artifacts,
        FileStream claim)
    {
        RunIdentity = runIdentity;
        Artifacts = artifacts;
        _claim = claim;
    }

    public Guid RunIdentity { get; }

    public string RunId => RunIdentity.ToString("N");

    public TestArtifactLayout Artifacts { get; }

    public static TestRunReservation Create(
        string repositoryRoot,
        DateTimeOffset requestedAtUtc,
        IReadOnlyList<TestWorkItem> workItems)
    {
        var root = RepositoryPaths.NormalizeRoot(repositoryRoot, nameof(repositoryRoot));
        var generatedRoot = Path.Combine(root, "build", "test-runs");
        RepositoryEntryGuard.CreateDirectoryPath(root, generatedRoot);

        for (var attempt = 0; attempt < 16; attempt++)
        {
            var identity = Guid.NewGuid();
            var shortIdentity = identity.ToString("N")[..16];
            var claimPath = Path.Combine(generatedRoot, $".{shortIdentity}.claim");
            FileStream? claim = null;
            string? runDirectory = null;
            var createdRunDirectory = false;
            try
            {
                claim = new FileStream(
                    claimPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose | FileOptions.WriteThrough);
                var directoryName = TestArtifactLayout.CreateRunDirectoryName(
                    requestedAtUtc,
                    identity);
                runDirectory = Path.Combine(generatedRoot, directoryName);
                if (Directory.Exists(runDirectory) || File.Exists(runDirectory))
                {
                    claim.Dispose();
                    continue;
                }

                Directory.CreateDirectory(runDirectory);
                createdRunDirectory = true;
                var artifacts = TestArtifactLayout.Create(root, runDirectory, workItems);
                return new TestRunReservation(identity, artifacts, claim);
            }
            catch (IOException) when (claim is null && File.Exists(claimPath))
            {
                continue;
            }
            catch
            {
                claim?.Dispose();
                if (createdRunDirectory && runDirectory is not null &&
                    (Directory.Exists(runDirectory) || File.Exists(runDirectory)))
                {
                    RepositoryTree.DeleteContained(generatedRoot, runDirectory);
                }

                throw;
            }
        }

        throw new IOException("Could not reserve a unique repository-local test run directory.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _claim.Dispose();
    }
}
