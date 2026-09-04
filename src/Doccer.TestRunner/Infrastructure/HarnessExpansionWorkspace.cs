using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace Doccer.TestRunner;

internal sealed class HarnessExpansionWorkspace : IDisposable
{
    private HarnessExpansionWorkspace(
        string repositoryRoot,
        string path,
        IReadOnlyDictionary<string, string> environment)
    {
        RepositoryRoot = repositoryRoot;
        Path = path;
        Environment = environment;
    }

    public string RepositoryRoot { get; }

    public string Path { get; }

    public IReadOnlyDictionary<string, string> Environment { get; }

    public static HarnessExpansionWorkspace Create(string repositoryRoot)
    {
        var root = RepositoryPaths.NormalizeRoot(repositoryRoot, nameof(repositoryRoot));
        var buildRoot = System.IO.Path.Combine(root, "build");
        RepositoryEntryGuard.CreateDirectoryPath(root, buildRoot);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var name = $"hx-{Guid.NewGuid():N}"[..19];
            var path = System.IO.Path.Combine(buildRoot, name);
            if (Directory.Exists(path) || File.Exists(path))
            {
                continue;
            }

            if (path.Length > TestArtifactLayout.MaximumManagedPathLength)
            {
                throw new PathTooLongException(
                    $"Runner-managed paths cannot exceed " +
                    $"{TestArtifactLayout.MaximumManagedPathLength} characters: '{path}'.");
            }

            try
            {
                Directory.CreateDirectory(path);
                RepositoryEntryGuard.RequireExistingWithoutReparsePoints(
                    root,
                    path,
                    directory: true,
                    nameof(path));
            }
            catch
            {
                RepositoryTree.DeleteContained(root, path);
                throw;
            }

            var environment = new ReadOnlyDictionary<string, string>(
                new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["TEMP"] = path,
                    ["TMP"] = path,
                    ["TMPDIR"] = path,
                });
            return new HarnessExpansionWorkspace(root, path, environment);
        }

        throw new IOException("A unique repository-local harness expansion workspace could not be created.");
    }

    public void Dispose()
    {
        RepositoryTree.DeleteContained(RepositoryRoot, Path);
    }
}
