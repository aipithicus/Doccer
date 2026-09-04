using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Doccer.TestRunner;

internal sealed class ExecutableHarnessCatalogValidationException : Exception
{
    public ExecutableHarnessCatalogValidationException(string message)
        : base(message)
    {
    }

    public ExecutableHarnessCatalogValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed record ExecutableHarnessCatalog(
    int SchemaVersion,
    string Protocol,
    string SuiteId,
    IReadOnlyList<ExecutableHarnessCatalogCase> Cases);

internal sealed record ExecutableHarnessCatalogCase(
    int Ordinal,
    string Id,
    string DisplayName,
    TestConcurrency Concurrency);

internal static class ExecutableHarnessCatalogParser
{
    public const int SchemaVersion = 1;
    public const string Protocol = "doccer-test-harness";

    public static ExecutableHarnessCatalog Parse(string json)
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
            if (syntax.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ExecutableHarnessCatalogValidationException(
                    "The executable-harness catalog must be a JSON object.");
            }

            RejectDuplicateProperties(syntax.RootElement, "$");
            var document = TestRunnerJson.Deserialize<CatalogDocument>(json)
                ?? throw new ExecutableHarnessCatalogValidationException(
                    "The executable-harness catalog must be a JSON object.");
            return Validate(document);
        }
        catch (ExecutableHarnessCatalogValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ExecutableHarnessCatalogValidationException(
                $"The executable-harness catalog is not valid schema-versioned JSON: " +
                exception.Message,
                exception);
        }
    }

    private static ExecutableHarnessCatalog Validate(CatalogDocument document)
    {
        if (document.SchemaVersion != SchemaVersion)
        {
            throw new ExecutableHarnessCatalogValidationException(
                $"Unsupported executable-harness catalog schema version " +
                $"'{document.SchemaVersion?.ToString() ?? "missing"}'.");
        }

        if (!string.Equals(document.Protocol, Protocol, StringComparison.Ordinal))
        {
            throw new ExecutableHarnessCatalogValidationException(
                $"Executable-harness catalog protocol must be '{Protocol}'.");
        }

        var suiteId = RequiredText(document.SuiteId, "Catalog suite ID", 256);
        if (document.Cases is null || document.Cases.Count == 0)
        {
            throw new ExecutableHarnessCatalogValidationException(
                "The executable-harness catalog must contain at least one case.");
        }

        if (document.Cases.Count > TestArtifactLayout.MaximumWorkItemCount)
        {
            throw new ExecutableHarnessCatalogValidationException(
                $"An executable-harness catalog cannot contain more than " +
                $"{TestArtifactLayout.MaximumWorkItemCount} cases.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cases = new ExecutableHarnessCatalogCase[document.Cases.Count];
        for (var index = 0; index < document.Cases.Count; index++)
        {
            var ordinal = index + 1;
            var source = document.Cases[index]
                ?? throw new ExecutableHarnessCatalogValidationException(
                    $"Catalog case {ordinal} cannot be null.");
            if (source.Ordinal != ordinal)
            {
                throw new ExecutableHarnessCatalogValidationException(
                    $"Catalog case {ordinal} must declare ordinal {ordinal}; got " +
                    $"'{source.Ordinal?.ToString() ?? "missing"}'.");
            }

            var id = RequiredText(source.Id, $"Catalog case {ordinal} ID", 256);
            if (!ids.Add(id))
            {
                throw new ExecutableHarnessCatalogValidationException(
                    $"Duplicate executable-harness case ID '{id}'.");
            }

            var displayName = RequiredText(
                source.DisplayName,
                $"Catalog case {ordinal} display name",
                1024);
            var concurrency = source.Concurrency switch
            {
                "parallel" => TestConcurrency.Parallel,
                "exclusive" => TestConcurrency.Exclusive,
                null => throw new ExecutableHarnessCatalogValidationException(
                    $"Catalog case {ordinal} must declare 'concurrency'."),
                _ => throw new ExecutableHarnessCatalogValidationException(
                    $"Catalog case {ordinal} concurrency must be 'parallel' or 'exclusive'."),
            };
            cases[index] = new ExecutableHarnessCatalogCase(
                ordinal,
                id,
                displayName,
                concurrency);
        }

        return new ExecutableHarnessCatalog(
            SchemaVersion,
            Protocol,
            suiteId,
            Array.AsReadOnly(cases));
    }

    private static string RequiredText(string? value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ExecutableHarnessCatalogValidationException($"{name} is required.");
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ExecutableHarnessCatalogValidationException(
                $"{name} cannot have leading or trailing whitespace.");
        }

        if (value.Length > maximumLength)
        {
            throw new ExecutableHarnessCatalogValidationException(
                $"{name} cannot exceed {maximumLength} characters.");
        }

        if (value.Any(char.IsControl))
        {
            throw new ExecutableHarnessCatalogValidationException(
                $"{name} cannot contain control characters.");
        }

        return value;
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
                    throw new ExecutableHarnessCatalogValidationException(
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

    private sealed class CatalogDocument
    {
        public int? SchemaVersion { get; init; }

        public string? Protocol { get; init; }

        public string? SuiteId { get; init; }

        public List<CatalogCaseDocument?>? Cases { get; init; }
    }

    private sealed class CatalogCaseDocument
    {
        public int? Ordinal { get; init; }

        public string? Id { get; init; }

        public string? DisplayName { get; init; }

        public string? Concurrency { get; init; }
    }
}
