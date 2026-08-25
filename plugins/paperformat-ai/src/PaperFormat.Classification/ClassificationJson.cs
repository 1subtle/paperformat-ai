using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaperFormat.Domain;

namespace PaperFormat.Classification;

/// <summary>
/// Canonical, content-safe JSON serialization for classification sets.
/// </summary>
public static class ClassificationJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ClassificationSet classifications)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        return JsonSerializer.Serialize(classifications, Options);
    }

    public static ClassificationSet Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ClassificationContract contract =
            JsonSerializer.Deserialize<ClassificationContract>(json, Options)
            ?? throw new JsonException(
                "The classification JSON was empty.");
        return new ClassificationSet(
            contract.Revision,
            contract.Elements.Select(
                element => new DocumentElement(
                    element.ElementId,
                    element.Location,
                    element.Kind,
                    element.Confidence,
                    element.Status,
                    element.Reasons,
                    element.TextLength,
                    element.SourceStyleId)));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = true,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed record ClassificationContract
    {
        public required int Revision { get; init; }

        public required DocumentElementContract[] Elements { get; init; }
    }

    private sealed record DocumentElementContract
    {
        public required string ElementId { get; init; }

        public required StructuralLocation Location { get; init; }

        public required ManuscriptElementKind Kind { get; init; }

        public required decimal Confidence { get; init; }

        public required ClassificationStatus Status { get; init; }

        public required ClassificationReason[] Reasons { get; init; }

        public required int TextLength { get; init; }

        public string? SourceStyleId { get; init; }
    }
}
