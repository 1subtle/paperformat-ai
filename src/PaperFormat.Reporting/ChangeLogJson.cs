using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaperFormat.Domain;

namespace PaperFormat.Reporting;

/// <summary>
/// Canonical JSON serialization for deterministic repair change logs.
/// </summary>
public static class ChangeLogJson
{
    private static readonly JsonSerializerOptions OptionsValue = CreateOptions();

    public static string Serialize(ChangeLog changeLog)
    {
        ArgumentNullException.ThrowIfNull(changeLog);
        return JsonSerializer.Serialize(changeLog, OptionsValue);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = true,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
