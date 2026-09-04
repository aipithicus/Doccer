using System;
using System.IO;

namespace Doccer.TestRunner;

internal static class RepositoryEntryGuard
{
    public static void CreateDirectoryPath(string repositoryRoot, string target)
    {
        var root = RepositoryPaths.NormalizeRoot(repositoryRoot, nameof(repositoryRoot));
        var resolved = RepositoryPaths.ResolveContained(
            root,
            target,
            nameof(target),
            allowRoot: true);
        var relative = Path.GetRelativePath(root, resolved);
        if (relative == ".")
        {
            RequireDirectory(root, nameof(repositoryRoot));
            return;
        }

        var current = root;
        foreach (var component in relative.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                Directory.CreateDirectory(current);
            }

            RequireDirectory(current, nameof(target));
        }
    }

    public static void RequireExistingWithoutReparsePoints(
        string repositoryRoot,
        string target,
        bool directory,
        string parameterName)
    {
        var root = RepositoryPaths.NormalizeRoot(repositoryRoot, nameof(repositoryRoot));
        var resolved = RepositoryPaths.ResolveContained(
            root,
            target,
            parameterName,
            allowRoot: directory);
        var relative = Path.GetRelativePath(root, resolved);
        if (relative == ".")
        {
            if (!directory)
            {
                throw new IOException("A file path cannot resolve to the repository root.");
            }

            RequireDirectory(root, parameterName);
            return;
        }

        var current = root;
        foreach (var component in relative.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"Repository path '{Path.GetRelativePath(root, current)}' cannot be a reparse point.");
            }
        }

        var targetAttributes = File.GetAttributes(resolved);
        var isDirectory = (targetAttributes & FileAttributes.Directory) != 0;
        if (directory != isDirectory)
        {
            throw new IOException(directory
                ? $"Repository path '{Path.GetRelativePath(root, resolved)}' must be a directory."
                : $"Repository path '{Path.GetRelativePath(root, resolved)}' must be a file.");
        }
    }

    private static void RequireDirectory(string path, string parameterName)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new ArgumentException($"Path '{path}' must be a directory.", parameterName);
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException(
                $"Runner-managed directory '{path}' cannot be a reparse point.",
                parameterName);
        }
    }
}
