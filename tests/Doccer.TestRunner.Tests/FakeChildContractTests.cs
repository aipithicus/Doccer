using System;
using System.Collections.Generic;
using System.IO;

namespace Doccer.TestRunner.Tests;

internal static partial class Program
{
    private static void FakeChildIsControllableAndContainsArtifacts()
    {
        var passed = RunFakeChild(Array.Empty<string>());
        Equal(0, passed.ExitCode, "fake child default pass");
        Equal(string.Empty, passed.StandardOutput, "fake child default stdout");
        Equal(string.Empty, passed.StandardError, "fake child default stderr");

        var emittedFailure = RunFakeChild(
            new[]
            {
                "--stdout", "visible output",
                "--stderr", "visible error",
                "--delay-milliseconds", "1",
                "--exit-code", "7",
            });
        Equal(7, emittedFailure.ExitCode, "fake child selected exit code");
        Contains("visible output", emittedFailure.StandardOutput, "fake child stdout");
        Contains("visible error", emittedFailure.StandardError, "fake child stderr");

        var help = RunFakeChild(new[] { "--help" });
        Equal(0, help.ExitCode, "fake child help exit");
        Contains("--spawn-child-milliseconds", help.StandardOutput, "fake child spawn surface");
        Contains("--ignore-cancel", help.StandardOutput, "fake child cancellation surface");

        var unknown = RunFakeChild(new[] { "--unknown" });
        Equal(2, unknown.ExitCode, "fake child unknown option exit");
        Contains("Unknown option", unknown.StandardError, "fake child unknown option message");
        var duplicate = RunFakeChild(new[] { "--stdout", "one", "--stdout", "two" });
        Equal(2, duplicate.ExitCode, "fake child duplicate option exit");
        Contains("Duplicate option", duplicate.StandardError, "fake child duplicate option message");
        var orphanedContent = RunFakeChild(new[] { "--artifact-content", "artifact" });
        Equal(2, orphanedContent.ExitCode, "orphaned artifact content exit");
        Contains("requires --artifact", orphanedContent.StandardError, "orphaned artifact content message");

        var testRoot = Path.Combine(
            AppContext.BaseDirectory,
            $"fake-child-contract-{Guid.NewGuid():N}");
        var artifactDirectory = Path.Combine(testRoot, "artifacts");
        True(
            RepositoryPaths.Contains(AppContext.BaseDirectory, testRoot, allowRoot: false),
            "fake child test root remains in repository build output");

        try
        {
            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ChildEnvironmentContract.CaseArtifactDirectory] = artifactDirectory,
            };
            var artifact = RunFakeChild(
                new[]
                {
                    "--artifact", "nested/evidence.txt",
                    "--artifact-content", "contained evidence",
                },
                environment);
            var artifactPath = Path.Combine(artifactDirectory, "nested", "evidence.txt");
            Equal(0, artifact.ExitCode, "fake child artifact exit");
            True(File.Exists(artifactPath), "fake child artifact exists");
            Equal("contained evidence", File.ReadAllText(artifactPath), "fake child artifact content");

            var escape = RunFakeChild(
                new[] { "--artifact", "../escape.txt" },
                environment);
            Equal(2, escape.ExitCode, "fake child artifact escape exit");
            Contains("canonical", escape.StandardError, "fake child artifact escape message");
            True(!File.Exists(Path.Combine(testRoot, "escape.txt")), "escaped artifact was not written");

            var tooDeep = RunFakeChild(
                new[] { "--artifact", "one/two/three/evidence.txt" },
                environment);
            Equal(2, tooDeep.ExitCode, "fake child artifact depth exit");
            Contains("components", tooDeep.StandardError, "fake child artifact depth message");

            var missingEnvironment = RunFakeChild(new[] { "--artifact", "evidence.txt" });
            Equal(2, missingEnvironment.ExitCode, "fake child missing artifact environment exit");
            Contains(
                ChildEnvironmentContract.CaseArtifactDirectory,
                missingEnvironment.StandardError,
                "fake child missing artifact environment message");
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static FakeChildRunResult RunFakeChild(
        string[] args,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        environment ??= new Dictionary<string, string>();

        var exitCode = global::Doccer.TestRunner.FakeChild.Program.RunAsync(
                args,
                stdout,
                stderr,
                name => environment.TryGetValue(name, out var value) ? value : null)
            .GetAwaiter()
            .GetResult();
        return new FakeChildRunResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed record FakeChildRunResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
