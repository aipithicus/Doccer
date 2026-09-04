using System;
using System.IO;

namespace Doccer.TestRunner;

internal static class RepositoryPaths
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string NormalizeRoot(string root, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("A root path is required.", parameterName);
        }

        if (!Path.IsPathRooted(root))
        {
            throw new ArgumentException("The root path must be absolute.", parameterName);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public static string NormalizeRelative(
        string root,
        string relativePath,
        string parameterName,
        bool allowRoot)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A repository-relative path is required.", parameterName);
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("The path must be repository-relative.", parameterName);
        }

        var normalizedRoot = NormalizeRoot(root, nameof(root));
        var resolved = Path.GetFullPath(relativePath, normalizedRoot);
        RequireContained(normalizedRoot, resolved, parameterName, allowRoot);

        var normalized = Path.GetRelativePath(normalizedRoot, resolved).Replace('\\', '/');
        return normalized.Length == 0 ? "." : normalized;
    }

    public static string ResolveContained(
        string root,
        string candidate,
        string parameterName,
        bool allowRoot)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new ArgumentException("A path is required.", parameterName);
        }

        var normalizedRoot = NormalizeRoot(root, nameof(root));
        var resolved = Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(candidate, normalizedRoot);
        RequireContained(normalizedRoot, resolved, parameterName, allowRoot);
        return resolved;
    }

    public static bool Contains(string root, string candidate, bool allowRoot)
    {
        var normalizedRoot = NormalizeRoot(root, nameof(root));
        var resolved = Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(candidate, normalizedRoot);

        if (allowRoot && string.Equals(normalizedRoot, resolved, PathComparison))
        {
            return true;
        }

        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return resolved.StartsWith(prefix, PathComparison);
    }

    public static bool Same(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            PathComparison);

    private static void RequireContained(
        string root,
        string candidate,
        string parameterName,
        bool allowRoot)
    {
        if (!Contains(root, candidate, allowRoot))
        {
            throw new ArgumentException($"The path must remain beneath '{root}'.", parameterName);
        }
    }
}
