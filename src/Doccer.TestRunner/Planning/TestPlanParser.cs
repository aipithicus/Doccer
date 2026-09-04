using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Doccer.TestRunner;

internal sealed class TestPlanValidationException : Exception
{
    public TestPlanValidationException(string message)
        : base(message)
    {
    }

    public TestPlanValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class TestPlanParser
{
    public static TestPlan Parse(string json, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var syntax = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            RejectDuplicateProperties(syntax.RootElement, "$");

            var document = TestRunnerJson.Deserialize<TestPlanDocument>(json)
                ?? throw new TestPlanValidationException("The test plan must be a JSON object.");
            return Validate(document, repositoryRoot);
        }
        catch (TestPlanValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new TestPlanValidationException(
                $"The test plan is not valid schema-versioned JSON: {exception.Message}",
                exception);
        }
    }

    public static string Serialize(TestPlan plan) =>
        TestRunnerJson.Serialize(plan);

    private static TestPlan Validate(TestPlanDocument document, string repositoryRoot)
    {
        var root = RepositoryPaths.NormalizeRoot(repositoryRoot, nameof(repositoryRoot));

        if (document.SchemaVersion != TestRunnerProtocol.SchemaVersion)
        {
            throw new TestPlanValidationException(
                $"Unsupported test plan schema version '{document.SchemaVersion}'.");
        }

        if (!string.Equals(document.Protocol, TestRunnerProtocol.Plan, StringComparison.Ordinal))
        {
            throw new TestPlanValidationException(
                $"Test plan protocol must be '{TestRunnerProtocol.Plan}'.");
        }

        if (document.Entries is null || document.Entries.Count == 0)
        {
            throw new TestPlanValidationException("The test plan must contain at least one entry.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var entries = new TestPlanEntry[document.Entries.Count];
        for (var index = 0; index < document.Entries.Count; index++)
        {
            var source = document.Entries[index]
                ?? throw new TestPlanValidationException($"Plan entry {index + 1} cannot be null.");
            var entry = ValidateEntry(source, root, index + 1);
            if (!ids.Add(entry.Id))
            {
                throw new TestPlanValidationException($"Duplicate plan entry ID '{entry.Id}'.");
            }

            entries[index] = entry;
        }

        return new TestPlan(
            TestRunnerProtocol.SchemaVersion,
            TestRunnerProtocol.Plan,
            Array.AsReadOnly(entries));
    }

    private static TestPlanEntry ValidateEntry(
        TestPlanEntryDocument source,
        string repositoryRoot,
        int ordinal)
    {
        var prefix = $"Plan entry {ordinal}";
        var id = RequiredText(source.Id, $"{prefix} ID", maximumLength: 256);
        var displayName = RequiredText(
            source.DisplayName,
            $"{prefix} display name",
            maximumLength: 1024);
        var kind = source.Kind switch
        {
            "command" => TestPlanEntryKind.Command,
            "executable_harness" => TestPlanEntryKind.ExecutableHarness,
            null => throw new TestPlanValidationException($"{prefix} must declare 'kind'."),
            _ => throw new TestPlanValidationException(
                $"{prefix} kind must be 'command' or 'executable_harness'."),
        };
        var workingDirectory = NormalizeRepositoryRelative(
            repositoryRoot,
            RequiredText(source.WorkingDirectory, $"{prefix} working directory", 1024),
            $"{prefix} working directory",
            allowRoot: true);
        var arguments = FreezeArguments(source.Arguments, prefix);
        var environment = FreezeEnvironment(source.Environment, prefix);
        var group = source.Group is null
            ? null
            : RequiredText(source.Group, $"{prefix} group", maximumLength: 256);

        if (source.TimeoutMilliseconds is <= 0)
        {
            throw new TestPlanValidationException(
                $"{prefix} timeoutMilliseconds must be greater than zero when supplied.");
        }

        return kind switch
        {
            TestPlanEntryKind.Command => ValidateCommand(
                source,
                id,
                displayName,
                arguments,
                workingDirectory,
                environment,
                group,
                prefix,
                repositoryRoot),
            TestPlanEntryKind.ExecutableHarness => ValidateHarness(
                source,
                id,
                displayName,
                arguments,
                workingDirectory,
                environment,
                group,
                prefix,
                repositoryRoot),
            _ => throw new TestPlanValidationException($"{prefix} has an unsupported kind."),
        };
    }

    private static TestPlanEntry ValidateCommand(
        TestPlanEntryDocument source,
        string id,
        string displayName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        string? group,
        string prefix,
        string repositoryRoot)
    {
        if (source.Project is not null)
        {
            throw new TestPlanValidationException($"{prefix} command cannot declare 'project'.");
        }

        var executable = RequiredText(source.Executable, $"{prefix} executable", 1024);
        if (executable.Contains('/') || executable.Contains('\\'))
        {
            executable = NormalizeRepositoryRelative(
                repositoryRoot,
                executable,
                $"{prefix} executable",
                allowRoot: false);
        }

        var concurrency = source.Concurrency switch
        {
            "parallel" => TestConcurrency.Parallel,
            "exclusive" => TestConcurrency.Exclusive,
            null => throw new TestPlanValidationException(
                $"{prefix} command must declare 'concurrency'."),
            _ => throw new TestPlanValidationException(
                $"{prefix} command concurrency must be 'parallel' or 'exclusive'."),
        };

        return new TestPlanEntry(
            id,
            displayName,
            TestPlanEntryKind.Command,
            Project: null,
            executable,
            arguments,
            workingDirectory,
            environment,
            source.TimeoutMilliseconds,
            group,
            concurrency);
    }

    private static TestPlanEntry ValidateHarness(
        TestPlanEntryDocument source,
        string id,
        string displayName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        string? group,
        string prefix,
        string repositoryRoot)
    {
        if (source.Executable is not null)
        {
            throw new TestPlanValidationException(
                $"{prefix} executable harness cannot declare 'executable'.");
        }

        if (source.Concurrency is not null)
        {
            throw new TestPlanValidationException(
                $"{prefix} executable harness receives concurrency from its case catalog.");
        }

        if (arguments.Count != 0)
        {
            throw new TestPlanValidationException(
                $"{prefix} executable harness arguments are runner-owned.");
        }

        var project = NormalizeRepositoryRelative(
            repositoryRoot,
            RequiredText(source.Project, $"{prefix} project", 1024),
            $"{prefix} project",
            allowRoot: false);
        if (!project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new TestPlanValidationException(
                $"{prefix} executable harness project must name a .csproj file.");
        }

        return new TestPlanEntry(
            id,
            displayName,
            TestPlanEntryKind.ExecutableHarness,
            project,
            Executable: null,
            arguments,
            workingDirectory,
            environment,
            source.TimeoutMilliseconds,
            group,
            Concurrency: null);
    }

    private static IReadOnlyList<string> FreezeArguments(
        IReadOnlyList<string?>? source,
        string prefix)
    {
        if (source is null)
        {
            throw new TestPlanValidationException($"{prefix} must declare an 'arguments' array.");
        }

        var arguments = new string[source.Count];
        for (var index = 0; index < source.Count; index++)
        {
            var argument = source[index]
                ?? throw new TestPlanValidationException(
                    $"{prefix} argument {index + 1} cannot be null.");
            if (argument.Any(char.IsControl))
            {
                throw new TestPlanValidationException(
                    $"{prefix} argument {index + 1} cannot contain control characters.");
            }

            arguments[index] = argument;
        }

        return Array.AsReadOnly(arguments);
    }

    private static IReadOnlyDictionary<string, string> FreezeEnvironment(
        IReadOnlyDictionary<string, string?>? source,
        string prefix)
    {
        if (source is null)
        {
            throw new TestPlanValidationException($"{prefix} must declare an 'environment' object.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            var name = RequiredText(pair.Key, $"{prefix} environment name", 256);
            if (!names.Add(name))
            {
                throw new TestPlanValidationException(
                    $"{prefix} contains environment names that differ only by case: '{name}'.");
            }

            if (ChildEnvironmentContract.IsReserved(name))
            {
                throw new TestPlanValidationException(
                    $"{prefix} cannot override reserved environment name '{name}'.");
            }

            var value = pair.Value
                ?? throw new TestPlanValidationException(
                    $"{prefix} environment value for '{name}' cannot be null.");
            if (value.Contains('\0'))
            {
                throw new TestPlanValidationException(
                    $"{prefix} environment value for '{name}' cannot contain a null character.");
            }

            values.Add(name, value);
        }

        return new ReadOnlyDictionary<string, string>(values);
    }

    private static string RequiredText(string? value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new TestPlanValidationException($"{name} is required.");
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new TestPlanValidationException($"{name} cannot have leading or trailing whitespace.");
        }

        if (value.Length > maximumLength)
        {
            throw new TestPlanValidationException(
                $"{name} cannot exceed {maximumLength} characters.");
        }

        if (value.Any(char.IsControl))
        {
            throw new TestPlanValidationException($"{name} cannot contain control characters.");
        }

        return value;
    }

    private static string NormalizeRepositoryRelative(
        string repositoryRoot,
        string value,
        string name,
        bool allowRoot)
    {
        try
        {
            return RepositoryPaths.NormalizeRelative(repositoryRoot, value, name, allowRoot);
        }
        catch (ArgumentException exception)
        {
            throw new TestPlanValidationException(exception.Message, exception);
        }
        catch (Exception exception) when (
            exception is NotSupportedException or PathTooLongException)
        {
            throw new TestPlanValidationException(exception.Message, exception);
        }
    }

    private static void RejectDuplicateProperties(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new TestPlanValidationException(
                        $"Duplicate JSON property '{property.Name}' at {path}.");
                }

                RejectDuplicateProperties(property.Value, $"{path}.{property.Name}");
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            RejectDuplicateProperties(item, $"{path}[{index++}]");
        }
    }

    private sealed class TestPlanDocument
    {
        public int SchemaVersion { get; init; }

        public string? Protocol { get; init; }

        public List<TestPlanEntryDocument?>? Entries { get; init; }
    }

    private sealed class TestPlanEntryDocument
    {
        public string? Id { get; init; }

        public string? DisplayName { get; init; }

        public string? Kind { get; init; }

        public string? Project { get; init; }

        public string? Executable { get; init; }

        public List<string?>? Arguments { get; init; }

        public string? WorkingDirectory { get; init; }

        public Dictionary<string, string?>? Environment { get; init; }

        public int? TimeoutMilliseconds { get; init; }

        public string? Group { get; init; }

        public string? Concurrency { get; init; }
    }
}
