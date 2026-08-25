using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaperFormat.Domain;

namespace PaperFormat.Rules;

/// <summary>
/// Canonical JSON serialization for rule packages.
/// </summary>
public static class RulePackageJson
{
    private static readonly JsonSerializerOptions OptionsValue = CreateOptions();

    public static JsonSerializerOptions Options => OptionsValue;

    public static string Serialize(RulePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return JsonSerializer.Serialize(package, OptionsValue);
    }

    public static RulePackage Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        RulePackageContract contract =
            JsonSerializer.Deserialize<RulePackageContract>(json, OptionsValue)
            ?? throw new JsonException("The rule package JSON was empty.");
        return new RulePackage(
            contract.PackageId,
            contract.Revision,
            contract.Name,
            contract.ProviderId,
            contract.ProviderVersion,
            contract.SourceReference,
            contract.Rules,
            contract.Notices,
            contract.SchemaVersion);
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

    private sealed record RulePackageContract
    {
        public required string SchemaVersion { get; init; }

        public required string PackageId { get; init; }

        public required int Revision { get; init; }

        public required string Name { get; init; }

        public required string ProviderId { get; init; }

        public required string ProviderVersion { get; init; }

        public required string SourceReference { get; init; }

        public required FormatRule[] Rules { get; init; }

        public required RulePackageNotice[] Notices { get; init; }
    }
}
