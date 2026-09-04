using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doccer.TestRunner;

internal sealed class TestArtifactValidationException : Exception
{
    public TestArtifactValidationException(string message)
        : base(message)
    {
    }
}

internal static class ArtifactCollectionInspector
{
    public static TestArtifactCapture? Inspect(TestCaseArtifactLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (File.Exists(layout.ArtifactDirectory))
        {
            throw new TestArtifactValidationException(
                "The assigned artifact path must be a directory, not a file.");
        }

        if (!Directory.Exists(layout.ArtifactDirectory))
        {
            return null;
        }

        var rootAttributes = File.GetAttributes(layout.ArtifactDirectory);
        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new TestArtifactValidationException(
                "The assigned artifact directory cannot be a reparse point.");
        }

        var files = new List<FileInfo>();
        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((layout.ArtifactDirectory, 0));
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory
                         .EnumerateFileSystemEntries(current.Directory)
                         .OrderByDescending(value => value, StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new TestArtifactValidationException(
                        "Child artifacts cannot contain reparse points.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    var depth = current.Depth + 1;
                    if (depth >= ArtifactPathContract.MaximumComponentCount)
                    {
                        throw new TestArtifactValidationException(
                            $"Child artifact directories cannot exceed " +
                            $"{ArtifactPathContract.MaximumComponentCount - 1} levels.");
                    }

                    pending.Push((entry, depth));
                    continue;
                }

                var relativePath = Path.GetRelativePath(layout.ArtifactDirectory, entry);
                var expected = ArtifactPathContract.ResolveFile(
                    layout.ArtifactDirectory,
                    relativePath);
                if (!RepositoryPaths.Same(expected, entry))
                {
                    throw new TestArtifactValidationException(
                        $"Artifact '{relativePath}' does not resolve canonically.");
                }

                files.Add(new FileInfo(entry));
                if (files.Count > TestCaptureContract.MaximumArtifactFileCount)
                {
                    throw new TestArtifactValidationException(
                        $"A case cannot retain more than " +
                        $"{TestCaptureContract.MaximumArtifactFileCount} artifact files.");
                }
            }
        }

        if (files.Count == 0)
        {
            RepositoryTree.DeleteContained(layout.CaseDirectory, layout.ArtifactDirectory);
            return null;
        }

        long totalBytes = 0;
        long largestFileBytes = 0;
        foreach (var file in files)
        {
            if (file.Length > TestCaptureContract.MaximumArtifactFileBytes)
            {
                throw new TestArtifactValidationException(
                    $"Artifact '{file.Name}' exceeds the " +
                    $"{TestCaptureContract.MaximumArtifactFileBytes}-byte file budget.");
            }

            totalBytes = checked(totalBytes + file.Length);
            largestFileBytes = Math.Max(largestFileBytes, file.Length);
            if (totalBytes > TestCaptureContract.MaximumArtifactTotalBytes)
            {
                throw new TestArtifactValidationException(
                    $"Case artifacts exceed the " +
                    $"{TestCaptureContract.MaximumArtifactTotalBytes}-byte total budget.");
            }
        }

        return TestArtifactCapture.Create(files.Count, totalBytes, largestFileBytes);
    }
}
