using System;
using System.IO;
using System.Reflection;

namespace Doccer.TestRunner;

internal static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
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

        stderr.WriteLine(
            $"Command '{args[0]}' is not available in the TestRunner contract scaffold. " +
            "CLI plan loading, child execution, and scheduling have not landed yet.");
        stderr.WriteLine("Run 'Doccer.TestRunner --help' for the available surface.");
        return 2;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine(
            """
            Doccer.TestRunner contract scaffold

            Usage:
              Doccer.TestRunner --help
              Doccer.TestRunner --version

            Versioned plan, run, event, result, compact artifact, bounded receipt, lifecycle,
            and child-environment contracts are installed. A completed run will emit one JSON
            receipt that points to its summary; captured child streams stay in selective case
            details. CLI plan loading, child execution, scheduling, and run writing have not
            landed yet.
            """);
    }
}
