using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doccer.TestRunner;

internal static class ArtifactPathContract
{
    public const int MaximumRelativePathLength = 120;
    public const int MaximumComponentLength = 64;
    public const int MaximumComponentCount = 3;

    private static readonly char[] Separators = { '/', '\\' };
    private static readonly char[] PortableInvalidCharacters = { '<', '>', ':', '"', '|', '?', '*' };
    private static readonly HashSet<string> ReservedWindowsNames = CreateReservedWindowsNames();

    public static string ResolveFile(string artifactDirectory, string relativePath)
    {
        var root = RepositoryPaths.NormalizeRoot(artifactDirectory, nameof(artifactDirectory));
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("An artifact path must be nonblank and relative.", nameof(relativePath));
        }

        if (relativePath.Length > MaximumRelativePathLength)
        {
            throw new ArgumentException(
                $"An artifact path cannot exceed {MaximumRelativePathLength} characters.",
                nameof(relativePath));
        }

        var components = relativePath.Split(Separators, StringSplitOptions.None);
        if (components.Length > MaximumComponentCount)
        {
            throw new ArgumentException(
                $"An artifact path cannot contain more than {MaximumComponentCount} components.",
                nameof(relativePath));
        }

        foreach (var component in components)
        {
            ValidateComponent(component, nameof(relativePath));
        }

        var portableRelativePath = Path.Combine(components);
        var resolved = RepositoryPaths.ResolveContained(
            root,
            portableRelativePath,
            nameof(relativePath),
            allowRoot: false);
        if (resolved.Length > TestArtifactLayout.MaximumManagedPathLength)
        {
            throw new ArgumentException(
                $"The resolved artifact path cannot exceed " +
                $"{TestArtifactLayout.MaximumManagedPathLength} characters.",
                nameof(relativePath));
        }

        return resolved;
    }

    private static void ValidateComponent(string component, string parameterName)
    {
        if (component.Length == 0 ||
            component is "." or ".." ||
            !string.Equals(component, component.Trim(), StringComparison.Ordinal) ||
            component.EndsWith(".", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Artifact path components must be nonblank canonical names.",
                parameterName);
        }

        if (component.Length > MaximumComponentLength)
        {
            throw new ArgumentException(
                $"Artifact path components cannot exceed {MaximumComponentLength} characters.",
                parameterName);
        }

        if (component.Any(char.IsControl) || component.IndexOfAny(PortableInvalidCharacters) >= 0)
        {
            throw new ArgumentException(
                "Artifact path components contain a nonportable character.",
                parameterName);
        }

        var firstDot = component.IndexOf('.');
        var deviceStem = (firstDot < 0 ? component : component[..firstDot]).TrimEnd(' ', '.');
        if (ReservedWindowsNames.Contains(deviceStem))
        {
            throw new ArgumentException(
                $"Artifact path component '{component}' is a reserved device name.",
                parameterName);
        }
    }

    private static HashSet<string> CreateReservedWindowsNames()
    {
        var names = new HashSet<string>(
            new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" },
            StringComparer.OrdinalIgnoreCase);
        for (var suffix = 1; suffix <= 9; suffix++)
        {
            names.Add($"COM{suffix}");
            names.Add($"LPT{suffix}");
        }

        return names;
    }
}
