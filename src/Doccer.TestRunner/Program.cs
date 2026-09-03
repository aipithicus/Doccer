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
            $"Command '{args[0]}' is not available in the TestRunner scaffold. " +
            "Catalog expansion and execution land in the next slice.");
        stderr.WriteLine("Run 'Doccer.TestRunner --help' for the available surface.");
        return 2;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine(
            """
            Doccer.TestRunner scaffold

            Usage:
              Doccer.TestRunner --help
              Doccer.TestRunner --version

            The project and verification boundary are installed. Catalog expansion,
            process scheduling, and run artifacts have not landed yet.
            """);
    }
}
