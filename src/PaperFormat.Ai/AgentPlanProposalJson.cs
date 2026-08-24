using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaperFormat.Domain;

namespace PaperFormat.Ai;

/// <summary>
/// Untrusted plan proposed by an external Agent before policy validation.
/// </summary>
public sealed record AgentPlanProposal
{
    public const string CurrentSchemaVersion = "2.0";

    public AgentPlanProposal(
        string sourceReportId,
        string sourceSha256,
        string providerId,
        string model,
        bool visualEvidenceUsed,
        bool externalProcessingConsent,
        IEnumerable<ProposedDirective> directives,
        string schemaVersion = CurrentSchemaVersion,
        IEnumerable<ProposedLayoutOperation>? layoutOperations = null)
    {
        SchemaVersion = Required(schemaVersion, nameof(schemaVersion));
        SourceReportId = Required(sourceReportId, nameof(sourceReportId));
        SourceSha256 = RequiredSha256(
            sourceSha256,
            nameof(sourceSha256));
        ProviderId = Required(providerId, nameof(providerId));
        Model = Required(model, nameof(model));
        VisualEvidenceUsed = visualEvidenceUsed;
        ExternalProcessingConsent = externalProcessingConsent;
        Directives = new ValueList<ProposedDirective>(
            directives ?? throw new ArgumentNullException(nameof(directives)));
        LayoutOperations = new ValueList<ProposedLayoutOperation>(
            layoutOperations ?? Array.Empty<ProposedLayoutOperation>());
    }

    public string SchemaVersion { get; }
    public string SourceReportId { get; }
    public string SourceSha256 { get; }
    public string ProviderId { get; }
    public string Model { get; }
    public bool VisualEvidenceUsed { get; }
    public bool ExternalProcessingConsent { get; }
    public ValueList<ProposedDirective> Directives { get; }
    public ValueList<ProposedLayoutOperation> LayoutOperations { get; }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static string RequiredSha256(
        string value,
        string parameterName)
    {
        string normalized = Required(value, parameterName);
        if (normalized.Length != 64
            || normalized.Any(
                character => character is not
                    (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "A source SHA-256 must contain 64 lowercase hexadecimal characters.",
                parameterName);
        }

        return normalized;
    }
}

public static class AgentPlanProposalJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(AgentPlanProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return JsonSerializer.Serialize(proposal, Options);
    }

    public static AgentPlanProposal Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        AgentPlanProposalContract contract =
            JsonSerializer.Deserialize<AgentPlanProposalContract>(
                json,
                Options)
            ?? throw new JsonException(
                "The Agent plan proposal JSON was empty.");
        return new AgentPlanProposal(
            contract.SourceReportId,
            contract.SourceSha256,
            contract.ProviderId,
            contract.Model,
            contract.VisualEvidenceUsed,
            contract.ExternalProcessingConsent,
            contract.Directives,
            contract.SchemaVersion,
            contract.LayoutOperations ?? Array.Empty<ProposedLayoutOperation>());
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

    private sealed record AgentPlanProposalContract
    {
        public required string SchemaVersion { get; init; }
        public required string SourceReportId { get; init; }
        public required string SourceSha256 { get; init; }
        public required string ProviderId { get; init; }
        public required string Model { get; init; }
        public required bool VisualEvidenceUsed { get; init; }
        public required bool ExternalProcessingConsent { get; init; }
        public required ProposedDirective[] Directives { get; init; }
        public ProposedLayoutOperation[]? LayoutOperations { get; init; }
    }
}
