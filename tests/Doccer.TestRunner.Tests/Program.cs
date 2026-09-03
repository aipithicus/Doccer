using System;
using System.IO;

namespace Doccer.TestRunner.Tests;

internal static class Program
{
    private static int _checks;

    public static int Main()
    {
        try
        {
            EmptyInvocationShowsTheScaffoldBoundary();
            HelpIsAnAvailableCommand();
            VersionIsAnAvailableCommand();
            ExecutionCommandsFailLoudlyUntilImplemented();
            Console.WriteLine($"doccer test runner scaffold: {_checks} checks passed");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void EmptyInvocationShowsTheScaffoldBoundary()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = global::Doccer.TestRunner.Program.Run(Array.Empty<string>(), stdout, stderr);

        Equal(0, exitCode, "empty invocation exit code");
        Contains("Doccer.TestRunner scaffold", stdout.ToString(), "empty invocation help");
        Equal(string.Empty, stderr.ToString(), "empty invocation stderr");
    }

    private static void HelpIsAnAvailableCommand()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = global::Doccer.TestRunner.Program.Run(new[] { "--help" }, stdout, stderr);

        Equal(0, exitCode, "help exit code");
        Contains("--version", stdout.ToString(), "help version surface");
        Equal(string.Empty, stderr.ToString(), "help stderr");
    }

    private static void VersionIsAnAvailableCommand()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = global::Doccer.TestRunner.Program.Run(new[] { "--version" }, stdout, stderr);

        Equal(0, exitCode, "version exit code");
        Contains("Doccer.TestRunner ", stdout.ToString(), "version output");
        Equal(string.Empty, stderr.ToString(), "version stderr");
    }

    private static void ExecutionCommandsFailLoudlyUntilImplemented()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = global::Doccer.TestRunner.Program.Run(new[] { "run" }, stdout, stderr);

        Equal(2, exitCode, "unavailable command exit code");
        Equal(string.Empty, stdout.ToString(), "unavailable command stdout");
        Contains("not available in the TestRunner scaffold", stderr.ToString(), "unavailable command error");
    }

    private static void Contains(string expected, string actual, string name)
    {
        _checks++;
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{name}: expected text containing '{expected}', got '{actual}'.");
        }
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        _checks++;
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'.");
        }
    }
}
