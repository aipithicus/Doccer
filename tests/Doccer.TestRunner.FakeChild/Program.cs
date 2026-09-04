using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Doccer.TestRunner.FakeChild;

internal static class Program
{
    public static Task<int> Main(string[] args) =>
        RunAsync(args, Console.Out, Console.Error, Environment.GetEnvironmentVariable);

    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        FakeChildOptions options;
        try
        {
            options = FakeChildOptions.Parse(args);
        }
        catch (FakeChildUsageException exception)
        {
            await stderr.WriteLineAsync(exception.Message).ConfigureAwait(false);
            await stderr.WriteLineAsync("Run 'Doccer.TestRunner.FakeChild --help' for usage.")
                .ConfigureAwait(false);
            return 2;
        }

        if (options.ShowHelp)
        {
            await stdout.WriteLineAsync(
                """
                Doccer.TestRunner.FakeChild

                Options:
                  --stdout <text>
                  --stdout-bytes <n>
                  --stderr <text>
                  --delay-milliseconds <n>
                  --exit-code <0..255>
                  --artifact <relative-path> [--artifact-content <text>]
                  --spawn-child-milliseconds <n>
                  --ignore-cancel
                """).ConfigureAwait(false);
            return 0;
        }

        ConsoleCancelEventHandler? cancelHandler = null;
        if (options.IgnoreCancel)
        {
            cancelHandler = (_, eventArgs) => eventArgs.Cancel = true;
            Console.CancelKeyPress += cancelHandler;
        }

        try
        {
            if (options.StandardOutput is not null)
            {
                await stdout.WriteLineAsync(options.StandardOutput).ConfigureAwait(false);
            }

            if (options.StandardOutputBytes > 0)
            {
                await WriteRepeatedAsync(stdout, 'o', options.StandardOutputBytes)
                    .ConfigureAwait(false);
            }

            if (options.StandardError is not null)
            {
                await stderr.WriteLineAsync(options.StandardError).ConfigureAwait(false);
            }

            if (options.ArtifactRelativePath is not null)
            {
                try
                {
                    await WriteArtifactAsync(
                            options.ArtifactRelativePath,
                            options.ArtifactContent,
                            getEnvironmentVariable)
                        .ConfigureAwait(false);
                }
                catch (FakeChildUsageException exception)
                {
                    await stderr.WriteLineAsync(exception.Message).ConfigureAwait(false);
                    return 2;
                }
            }

            using var descendant = options.SpawnChildMilliseconds is { } descendantDelay
                ? StartDescendant(descendantDelay, options.IgnoreCancel)
                : null;
            if (descendant is not null)
            {
                await stdout.WriteLineAsync($"spawned-child={descendant.Id}").ConfigureAwait(false);
            }

            if (options.DelayMilliseconds > 0)
            {
                await Task.Delay(options.DelayMilliseconds).ConfigureAwait(false);
            }

            if (descendant is not null)
            {
                await descendant.WaitForExitAsync().ConfigureAwait(false);
            }

            return options.ExitCode;
        }
        finally
        {
            if (cancelHandler is not null)
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
    }

    private static async Task WriteArtifactAsync(
        string relativePath,
        string content,
        Func<string, string?> getEnvironmentVariable)
    {
        var artifactRoot = getEnvironmentVariable("DOCCER_TEST_CASE_ARTIFACT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(artifactRoot) || !Path.IsPathRooted(artifactRoot))
        {
            throw new FakeChildUsageException(
                "Artifact writing requires an absolute DOCCER_TEST_CASE_ARTIFACT_DIRECTORY.");
        }

        string target;
        try
        {
            target = ArtifactPathContract.ResolveFile(artifactRoot, relativePath);
        }
        catch (ArgumentException exception)
        {
            throw new FakeChildUsageException(exception.Message);
        }

        if (Encoding.UTF8.GetByteCount(content) > TestCaptureContract.MaximumArtifactFileBytes)
        {
            throw new FakeChildUsageException(
                $"The artifact exceeds the {TestCaptureContract.MaximumArtifactFileBytes}-byte " +
                "single-file budget.");
        }

        var parent = Path.GetDirectoryName(target)
            ?? throw new FakeChildUsageException("The artifact path has no parent directory.");
        Directory.CreateDirectory(parent);
        await File.WriteAllTextAsync(target, content).ConfigureAwait(false);
    }

    private static Process StartDescendant(int delayMilliseconds, bool ignoreCancel)
    {
        var assemblyPath = typeof(Program).Assembly.Location;
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "dotnet";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = host,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--delay-milliseconds");
        startInfo.ArgumentList.Add(delayMilliseconds.ToString(CultureInfo.InvariantCulture));
        if (ignoreCancel)
        {
            startInfo.ArgumentList.Add("--ignore-cancel");
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("The fake descendant process did not start.");
    }

    private static async Task WriteRepeatedAsync(TextWriter writer, char value, int count)
    {
        var buffer = new char[Math.Min(count, 16 * 1024)];
        Array.Fill(buffer, value);
        while (count > 0)
        {
            var current = Math.Min(count, buffer.Length);
            await writer.WriteAsync(buffer.AsMemory(0, current)).ConfigureAwait(false);
            count -= current;
        }
    }

    private sealed record FakeChildOptions(
        string? StandardOutput,
        int StandardOutputBytes,
        string? StandardError,
        int DelayMilliseconds,
        int ExitCode,
        string? ArtifactRelativePath,
        string ArtifactContent,
        int? SpawnChildMilliseconds,
        bool IgnoreCancel,
        bool ShowHelp)
    {
        public static FakeChildOptions Parse(string[] args)
        {
            if (args.Length == 1 &&
                (StringComparer.Ordinal.Equals(args[0], "--help") ||
                 StringComparer.Ordinal.Equals(args[0], "-h")))
            {
                return new FakeChildOptions(
                    null,
                    0,
                    null,
                    0,
                    0,
                    null,
                    string.Empty,
                    null,
                    false,
                    true);
            }

            string? standardOutput = null;
            var standardOutputBytes = 0;
            string? standardError = null;
            var delayMilliseconds = 0;
            var exitCode = 0;
            string? artifactRelativePath = null;
            var artifactContent = "artifact";
            var artifactContentSpecified = false;
            int? spawnChildMilliseconds = null;
            var ignoreCancel = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < args.Length; index++)
            {
                var option = args[index];
                if (!seen.Add(option))
                {
                    throw new FakeChildUsageException($"Duplicate option '{option}'.");
                }

                switch (option)
                {
                    case "--stdout":
                        standardOutput = NextValue(args, ref index, option);
                        break;
                    case "--stdout-bytes":
                        standardOutputBytes = ParseInteger(
                            NextValue(args, ref index, option),
                            option,
                            minimum: 0,
                            maximum: 16 * 1024 * 1024);
                        break;
                    case "--stderr":
                        standardError = NextValue(args, ref index, option);
                        break;
                    case "--delay-milliseconds":
                        delayMilliseconds = ParseInteger(
                            NextValue(args, ref index, option),
                            option,
                            minimum: 0,
                            maximum: int.MaxValue);
                        break;
                    case "--exit-code":
                        exitCode = ParseInteger(
                            NextValue(args, ref index, option),
                            option,
                            minimum: 0,
                            maximum: 255);
                        break;
                    case "--artifact":
                        artifactRelativePath = NextValue(args, ref index, option);
                        break;
                    case "--artifact-content":
                        artifactContent = NextValue(args, ref index, option);
                        artifactContentSpecified = true;
                        break;
                    case "--spawn-child-milliseconds":
                        spawnChildMilliseconds = ParseInteger(
                            NextValue(args, ref index, option),
                            option,
                            minimum: 1,
                            maximum: int.MaxValue);
                        break;
                    case "--ignore-cancel":
                        ignoreCancel = true;
                        break;
                    default:
                        throw new FakeChildUsageException($"Unknown option '{option}'.");
                }
            }

            if (artifactRelativePath is null && artifactContentSpecified)
            {
                throw new FakeChildUsageException("--artifact-content requires --artifact.");
            }

            return new FakeChildOptions(
                standardOutput,
                standardOutputBytes,
                standardError,
                delayMilliseconds,
                exitCode,
                artifactRelativePath,
                artifactContent,
                spawnChildMilliseconds,
                ignoreCancel,
                ShowHelp: false);
        }

        private static string NextValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
            {
                throw new FakeChildUsageException($"Option '{option}' requires a value.");
            }

            return args[++index];
        }

        private static int ParseInteger(string value, string option, int minimum, int maximum)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                parsed < minimum ||
                parsed > maximum)
            {
                throw new FakeChildUsageException(
                    $"Option '{option}' requires an integer from {minimum} through {maximum}.");
            }

            return parsed;
        }
    }

    private sealed class FakeChildUsageException : Exception
    {
        public FakeChildUsageException(string message)
            : base(message)
        {
        }
    }
}
