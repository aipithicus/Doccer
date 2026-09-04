using System;

namespace Doccer.TestRunner;

internal static class TestRunDiagnostics
{
    private const int MaximumEvidenceMessageCharacters = 2048;

    public static string BoundMessage(string? message)
    {
        var normalized = string.IsNullOrWhiteSpace(message)
            ? "No diagnostic message was supplied."
            : string.Join(
                ' ',
                message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= MaximumEvidenceMessageCharacters
            ? normalized
            : normalized[..(MaximumEvidenceMessageCharacters - 3)] + "...";
    }
}
