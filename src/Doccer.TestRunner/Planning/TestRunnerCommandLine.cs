using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doccer.TestRunner;

internal sealed class TestRunnerUsageException : Exception
{
    public TestRunnerUsageException(string message)
        : base(message)
    {
    }

    public TestRunnerUsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed record TestRunOptions(
    string RepositoryRoot,
    string PlanRelativePath,
    string PlanPath,
    string Configuration,
    int MaxParallel);

internal static class TestRunnerCommandLine
{
    private static readonly char[] PortableInvalidNameCharacters =
        { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    public static TestRunOptions ParseRun(string[] args, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || !StringComparer.Ordinal.Equals(args[0], "run"))
        {
            throw new TestRunnerUsageException(
                "Usage: Doccer.TestRunner run --plan <repository-relative.json> " +
                "[--repository-root <path>] [--configuration <name>] " +
                "[--max-parallel <count>]");
        }

        string? plan = null;
        string? repositoryRoot = null;
        var configuration = "Debug";
        var maxParallel = TestSchedulingContract.DefaultMaxParallel;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (!seen.Add(option))
            {
                throw new TestRunnerUsageException($"Duplicate option '{option}'.");
            }

            switch (option)
            {
                case "--plan":
                    plan = NextValue(args, ref index, option);
                    break;
                case "--repository-root":
                    repositoryRoot = NextValue(args, ref index, option);
                    break;
                case "--configuration":
                    configuration = NextValue(args, ref index, option);
                    break;
                case "--max-parallel":
                    maxParallel = ParseMaximumParallelism(
                        NextValue(args, ref index, option));
                    break;
                default:
                    throw new TestRunnerUsageException($"Unknown option '{option}'.");
            }
        }

        if (plan is null)
        {
            throw new TestRunnerUsageException("The run command requires --plan.");
        }

        var currentRoot = RepositoryPaths.NormalizeRoot(currentDirectory, nameof(currentDirectory));
        string root;
        try
        {
            root = repositoryRoot is null
                ? currentRoot
                : RepositoryPaths.NormalizeRoot(
                    Path.IsPathRooted(repositoryRoot)
                        ? repositoryRoot
                        : Path.GetFullPath(repositoryRoot, currentRoot),
                    nameof(repositoryRoot));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new TestRunnerUsageException(exception.Message, exception);
        }

        string planRelativePath;
        try
        {
            planRelativePath = RepositoryPaths.NormalizeRelative(
                root,
                plan,
                nameof(plan),
                allowRoot: false);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new TestRunnerUsageException(exception.Message, exception);
        }

        if (!planRelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new TestRunnerUsageException("The test plan must name a .json file.");
        }

        if (string.IsNullOrWhiteSpace(configuration) ||
            !string.Equals(configuration, configuration.Trim(), StringComparison.Ordinal) ||
            configuration.Length > 64 ||
            configuration.Any(char.IsControl) ||
            configuration.IndexOfAny(PortableInvalidNameCharacters) >= 0 ||
            configuration.EndsWith(".", StringComparison.Ordinal))
        {
            throw new TestRunnerUsageException(
                "Configuration must be a trimmed portable name of at most 64 characters.");
        }

        return new TestRunOptions(
            root,
            planRelativePath,
            Path.GetFullPath(planRelativePath, root),
            configuration,
            maxParallel);
    }

    private static string NextValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new TestRunnerUsageException($"Option '{option}' requires a value.");
        }

        return args[++index];
    }

    private static int ParseMaximumParallelism(string value)
    {
        if (!int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < 1 ||
            parsed > TestSchedulingContract.MaximumParallelism)
        {
            throw new TestRunnerUsageException(
                $"--max-parallel must be an integer from 1 through " +
                $"{TestSchedulingContract.MaximumParallelism}.");
        }

        return parsed;
    }
}
