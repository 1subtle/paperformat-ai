using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaperFormat.Domain;

namespace PaperFormat.Reporting;

/// <summary>
/// Canonical JSON serialization for format-check reports.
/// </summary>
public static class CheckReportJson
{
    private static readonly JsonSerializerOptions OptionsValue = CreateOptions();

    public static JsonSerializerOptions Options => OptionsValue;

    public static string Serialize(CheckReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, OptionsValue);
    }

    public static CheckReport Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        CheckReportContract contract =
            JsonSerializer.Deserialize<CheckReportContract>(
                json,
                OptionsValue)
            ?? throw new JsonException("The check report JSON was empty.");
        return new CheckReport(
            contract.ReportId,
            contract.RulePackageId,
            contract.RulePackageRevision,
            contract.Status,
            contract.Summary,
            contract.Issues,
            contract.SkippedRules,
            contract.PendingElements,
            contract.SchemaVersion);
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

    private sealed record CheckReportContract
    {
        public required string SchemaVersion { get; init; }

        public required string ReportId { get; init; }

        public required string RulePackageId { get; init; }

        public required int RulePackageRevision { get; init; }

        public required CheckStatus Status { get; init; }

        public required CheckSummary Summary { get; init; }

        public required CheckIssue[] Issues { get; init; }

        public required SkippedRule[] SkippedRules { get; init; }

        public required PendingElement[] PendingElements { get; init; }
    }
}
