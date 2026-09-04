using System;
using System.Collections.Generic;
using System.IO;

namespace Doccer.TestRunner.Tests;

internal static partial class Program
{
    private const int MaximumReceiptLength = 768;
    private static int _checks;

    public static int Main(string[] args)
    {
        var showDetails = args.Length == 1 && StringComparer.Ordinal.Equals(args[0], "--details");
        if (args.Length != 0 && !showDetails)
        {
            Console.Error.WriteLine("Usage: Doccer.TestRunner.Tests [--details]");
            return 2;
        }

        try
        {
            EmptyInvocationShowsTheScaffoldBoundary();
            HelpIsAnAvailableCommand();
            VersionIsAnAvailableCommand();
            ExecutionCommandsFailLoudlyUntilImplemented();
            PlanSchemaAndCommandExpansionAreDeterministic();
            PlanValidationRejectsAmbiguityAndEscapes();
            ArtifactLayoutIsContainedAndCollisionSafe();
            ChildEnvironmentIsIsolatedAndReserved();
            EventContractAndLifecycleTransitionsAreExact();
            RunContractsAccountForEveryPlannedItem();
            ResultStatusAndExitPrecedenceAreExact();
            ReceiptAndDetailMaterializationAreBounded();
            FakeChildIsControllableAndContainsArtifacts();
            WriteReceipt($"doccer test receipt: status=passed suite=doccer.test-runner checks={_checks}");
            return 0;
        }
        catch (Exception exception)
        {
            WriteReceipt(
                $"doccer test receipt: status=failed suite=doccer.test-runner " +
                $"checks={_checks} error={exception.GetType().Name}: {exception.Message}",
                Console.Error);
            if (showDetails)
            {
                Console.Error.WriteLine(exception);
            }

            return 1;
        }
    }

    private static void WriteReceipt(string value, TextWriter? writer = null)
    {
        writer ??= Console.Out;
        var singleLine = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (singleLine.Length > MaximumReceiptLength)
        {
            singleLine = singleLine[..(MaximumReceiptLength - 3)] + "...";
        }

        writer.WriteLine(singleLine);
    }

    private static void EmptyInvocationShowsTheScaffoldBoundary()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = global::Doccer.TestRunner.Program.Run(Array.Empty<string>(), stdout, stderr);

        Equal(0, exitCode, "empty invocation exit code");
        Contains("Doccer.TestRunner contract scaffold", stdout.ToString(), "empty invocation help");
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
        Contains("not available in the TestRunner contract scaffold", stderr.ToString(), "unavailable command error");
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

    private static void True(bool condition, string name)
    {
        _checks++;
        if (!condition)
        {
            throw new InvalidOperationException($"{name}: expected true.");
        }
    }

    private static TException Throws<TException>(Action action, string name)
        where TException : Exception
    {
        _checks++;
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{name}: expected {typeof(TException).Name}, got {exception.GetType().Name}.",
                exception);
        }

        throw new InvalidOperationException(
            $"{name}: expected {typeof(TException).Name}, but no exception was thrown.");
    }

    private static void SequenceEqual<T>(
        IReadOnlyList<T> expected,
        IReadOnlyList<T> actual,
        string name)
    {
        Equal(expected.Count, actual.Count, $"{name} count");
        for (var index = 0; index < expected.Count; index++)
        {
            Equal(expected[index], actual[index], $"{name} item {index}");
        }
    }
}
