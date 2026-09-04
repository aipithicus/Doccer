using System.Text.Json;
using System.Text.Json.Serialization;

namespace Doccer.TestRunner;

internal static class TestRunnerJson
{
    private static readonly JsonSerializerOptions ReadOptions = CreateOptions(writeIndented: false);
    private static readonly JsonSerializerOptions WriteOptions = CreateOptions(writeIndented: true);

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, ReadOptions);

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, WriteOptions);

    public static string SerializeCompact<T>(T value) =>
        JsonSerializer.Serialize(value, ReadOptions);

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 64,
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = writeIndented,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }
}
