using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Doccer.TestRunner;

internal sealed class ExecutableHarnessResultValidationException : Exception
{
    public ExecutableHarnessResultValidationException(string message)
        : base(message)
    {
    }

    public ExecutableHarnessResultValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed record ExecutableHarnessCaseResult(
    int SchemaVersion,
    string Protocol,
    string SuiteId,
    string CaseId,
    TestExecutionStatus Status,
    int CheckCount,
    double ElapsedMilliseconds,
    string? ErrorType,
    string? ErrorMessage);

internal static class ExecutableHarnessResultParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static ExecutableHarnessCaseResult Parse(
        CapturedProcessStream standardOutput,
        CapturedProcessStream standardError,
        string expectedCaseId)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        RequiredText(expectedCaseId, "Expected case ID", 256);
        if (standardError.Evidence is not null || standardError.Content is not null)
        {
            throw new ExecutableHarnessResultValidationException(
                "An executable-harness result cannot write to stderr.");
        }

        if (standardOutput.Evidence?.Truncated == true)
        {
            throw new ExecutableHarnessResultValidationException(
                "The executable-harness result exceeded the process-stream capture limit.");
        }

        if (standardOutput.Evidence is null || standardOutput.Content is null)
        {
            throw new ExecutableHarnessResultValidationException(
                "The executable-harness result must emit one JSON document on stdout.");
        }

        if (standardOutput.Content.LongLength != standardOutput.Evidence.RetainedBytes)
        {
            throw new ExecutableHarnessResultValidationException(
                "The executable-harness result capture metadata is inconsistent.");
        }

        string json;
        try
        {
            json = StrictUtf8.GetString(standardOutput.Content);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ExecutableHarnessResultValidationException(
                "The executable-harness result stdout is not valid UTF-8.",
                exception);
        }

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
                throw new ExecutableHarnessResultValidationException(
                    "The executable-harness result must be a JSON object.");
            }

            RejectDuplicateProperties(syntax.RootElement, "$");
            var document = TestRunnerJson.Deserialize<ResultDocument>(json)
                ?? throw new ExecutableHarnessResultValidationException(
                    "The executable-harness result must be a JSON object.");
            return Validate(document, expectedCaseId);
        }
        catch (ExecutableHarnessResultValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ExecutableHarnessResultValidationException(
                $"The executable-harness result is not valid schema-versioned JSON: " +
                exception.Message,
                exception);
        }
    }

    private static ExecutableHarnessCaseResult Validate(
        ResultDocument document,
        string expectedCaseId)
    {
        if (document.SchemaVersion != ExecutableHarnessCatalogParser.SchemaVersion)
        {
            throw new ExecutableHarnessResultValidationException(
                $"Unsupported executable-harness result schema version " +
                $"'{document.SchemaVersion?.ToString() ?? "missing"}'.");
        }

        if (!string.Equals(
                document.Protocol,
                ExecutableHarnessCatalogParser.Protocol,
                StringComparison.Ordinal))
        {
            throw new ExecutableHarnessResultValidationException(
                $"Executable-harness result protocol must be " +
                $"'{ExecutableHarnessCatalogParser.Protocol}'.");
        }

        var suiteId = RequiredText(document.SuiteId, "Harness result suite ID", 256);
        var caseId = RequiredText(document.CaseId, "Harness result case ID", 256);
        if (!string.Equals(caseId, expectedCaseId, StringComparison.Ordinal))
        {
            throw new ExecutableHarnessResultValidationException(
                $"Executable-harness result named case '{caseId}' instead of " +
                $"'{expectedCaseId}'.");
        }

        var status = document.Status switch
        {
            "passed" => TestExecutionStatus.Passed,
            "failed" => TestExecutionStatus.Failed,
            null => throw new ExecutableHarnessResultValidationException(
                "The executable-harness result must declare 'status'."),
            _ => throw new ExecutableHarnessResultValidationException(
                "Executable-harness result status must be 'passed' or 'failed'."),
        };
        if (document.CheckCount is null or < 0)
        {
            throw new ExecutableHarnessResultValidationException(
                "Executable-harness result checkCount must be a nonnegative integer.");
        }

        if (document.ElapsedMilliseconds is null or < 0 ||
            !double.IsFinite(document.ElapsedMilliseconds.Value))
        {
            throw new ExecutableHarnessResultValidationException(
                "Executable-harness result elapsedMilliseconds must be a finite nonnegative number.");
        }

        var errorType = OptionalType(document.ErrorType);
        var errorMessage = OptionalMessage(document.ErrorMessage);
        if (status == TestExecutionStatus.Passed &&
            (errorType is not null || errorMessage is not null))
        {
            throw new ExecutableHarnessResultValidationException(
                "A passed executable-harness result cannot contain error evidence.");
        }

        return new ExecutableHarnessCaseResult(
            ExecutableHarnessCatalogParser.SchemaVersion,
            ExecutableHarnessCatalogParser.Protocol,
            suiteId,
            caseId,
            status,
            document.CheckCount.Value,
            document.ElapsedMilliseconds.Value,
            errorType,
            errorMessage);
    }

    private static string RequiredText(string? value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ExecutableHarnessResultValidationException($"{name} is required.");
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ExecutableHarnessResultValidationException(
                $"{name} cannot have leading or trailing whitespace.");
        }

        if (value.Length > maximumLength)
        {
            throw new ExecutableHarnessResultValidationException(
                $"{name} cannot exceed {maximumLength} characters.");
        }

        if (value.Any(char.IsControl))
        {
            throw new ExecutableHarnessResultValidationException(
                $"{name} cannot contain control characters.");
        }

        return value;
    }

    private static string? OptionalType(string? value) =>
        value is null ? null : RequiredText(value, "Harness result error type", 1024);

    private static string? OptionalMessage(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length > 8192)
        {
            throw new ExecutableHarnessResultValidationException(
                "Harness result error message cannot exceed 8192 characters.");
        }

        if (value.Contains('\0'))
        {
            throw new ExecutableHarnessResultValidationException(
                "Harness result error message cannot contain a null character.");
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
                    throw new ExecutableHarnessResultValidationException(
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

    private sealed class ResultDocument
    {
        public int? SchemaVersion { get; init; }

        public string? Protocol { get; init; }

        public string? SuiteId { get; init; }

        public string? CaseId { get; init; }

        public string? Status { get; init; }

        public int? CheckCount { get; init; }

        public double? ElapsedMilliseconds { get; init; }

        public string? ErrorType { get; init; }

        public string? ErrorMessage { get; init; }
    }
}
