using System;
using System.IO;
using System.Linq;

namespace Doccer.TestRunner;

internal static class RepositoryTree
{
    public static void DeleteContained(string containmentRoot, string target)
    {
        var resolved = RepositoryPaths.ResolveContained(
            containmentRoot,
            target,
            nameof(target),
            allowRoot: false);
        try
        {
            DeleteEntry(resolved);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static void DeleteEntry(string path)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
        if (!isDirectory)
        {
            File.Delete(path);
            return;
        }

        if (!isReparsePoint)
        {
            foreach (var entry in Directory
                         .EnumerateFileSystemEntries(path)
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                DeleteEntry(entry);
            }
        }

        Directory.Delete(path);
    }
}
