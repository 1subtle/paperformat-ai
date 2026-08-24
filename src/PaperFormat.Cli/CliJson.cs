using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaperFormat.Cli;

internal static class CliJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(object value) =>
        JsonSerializer.Serialize(value, Options);

    public static async Task WriteFileAsync(
        string path,
        object value,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException(
                "The output path has no directory.",
                nameof(path)));
        await File.WriteAllTextAsync(
            fullPath,
            Serialize(value) + Environment.NewLine,
            cancellationToken);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = true,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
