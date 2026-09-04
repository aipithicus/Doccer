using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Doccer.TestRunner;

internal static class Program
{
    private const int MaximumConsoleErrorLength = 768;

    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            return await RunAsync(
                    args,
                    Console.Out,
                    Console.Error,
                    cancellation.Token,
                    Environment.CurrentDirectory)
                .ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr) =>
        RunAsync(
                args,
                stdout,
                stderr,
                CancellationToken.None,
                Environment.CurrentDirectory)
            .GetAwaiter()
            .GetResult();

    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken,
        string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Length == 0 ||
            (args.Length == 1 &&
             (StringComparer.Ordinal.Equals(args[0], "help") ||
              StringComparer.Ordinal.Equals(args[0], "--help") ||
              StringComparer.Ordinal.Equals(args[0], "-h"))))
        {
            WriteHelp(stdout);
            return 0;
        }

        if (args.Length == 1 && StringComparer.Ordinal.Equals(args[0], "--version"))
        {
            var version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "unknown";
            stdout.WriteLine($"Doccer.TestRunner {version}");
            return 0;
        }

        if (!StringComparer.Ordinal.Equals(args[0], "run"))
        {
            WriteError(stderr, $"Unknown command '{args[0]}'.");
            return (int)TestRunExitCode.InvalidInvocation;
        }

        try
        {
            var options = TestRunnerCommandLine.ParseRun(args, currentDirectory);
            using var completion = await TestRunCoordinator.ExecuteAsync(
                    options,
                    TimeProvider.System,
                    cancellationToken)
                .ConfigureAwait(false);
            await stdout.WriteLineAsync(completion.Receipt.ToJsonLine()).ConfigureAwait(false);
            return completion.Receipt.ExitCode;
        }
        catch (TestRunnerUsageException exception)
        {
            WriteError(stderr, exception.Message);
            return (int)TestRunExitCode.InvalidInvocation;
        }
        catch (TestRunInfrastructureException exception)
        {
            WriteError(stderr, exception.Message);
            return (int)TestRunExitCode.InfrastructureError;
        }
        catch (OperationCanceledException)
        {
            WriteError(stderr, "Test plan expansion was canceled.");
            return (int)TestRunExitCode.Interrupted;
        }
        catch (Exception exception)
        {
            WriteError(
                stderr,
                $"The TestRunner encountered an infrastructure error: {exception.Message}");
            return (int)TestRunExitCode.InfrastructureError;
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine(
            """
            Doccer.TestRunner

            Usage:
              Doccer.TestRunner --help
              Doccer.TestRunner --version
              Doccer.TestRunner run --plan <repository-relative.json>
                [--repository-root <path>] [--configuration <name>] [--max-parallel <count>]

            The run command expands command entries and native executable-harness catalogs, then
            executes parallel work up to the configured bound with an empty-worker barrier around
            each exclusive item. Children are launched directly without a shell, disposable files
            remain beneath build, and stdout contains one JSON receipt pointing to the summary.
            Retention keeps the newest 16 recognized finalized runs while preserving active,
            partial, and unrecognized directories.
            """);
    }

    private static void WriteError(TextWriter writer, string message)
    {
        var normalized = string.Join(
            ' ',
            message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > MaximumConsoleErrorLength)
        {
            normalized = normalized[..(MaximumConsoleErrorLength - 3)] + "...";
        }

        writer.WriteLine(normalized);
    }
}
