using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaperFormat.Domain;

namespace PaperFormat.Reporting;

/// <summary>
/// Canonical JSON serialization for content-integrity reports.
/// </summary>
public static class IntegrityReportJson
{
    private static readonly JsonSerializerOptions OptionsValue = CreateOptions();

    public static string Serialize(IntegrityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, OptionsValue);
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
