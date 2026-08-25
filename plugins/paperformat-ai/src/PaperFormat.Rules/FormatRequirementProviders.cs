using PaperFormat.Domain;

namespace PaperFormat.Rules;

/// <summary>
/// Input understood by an <see cref="IFormatRequirementProvider"/>.
/// </summary>
public abstract record FormatRequirementSource;

/// <summary>
/// Selects a real, built-in format profile.
/// </summary>
public sealed record BuiltInFormatRequirementSource : FormatRequirementSource
{
    public BuiltInFormatRequirementSource(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException(
                "A built-in profile identifier is required.",
                nameof(profileId));
        }

        ProfileId = profileId;
    }

    public string ProfileId { get; }
}

/// <summary>
/// Supplies a parsed DOCX or DOTX template for deterministic rule extraction.
/// </summary>
public sealed record WordTemplateFormatRequirementSource
    : FormatRequirementSource
{
    public WordTemplateFormatRequirementSource(
        string sourceName,
        DocumentModel document)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException(
                "A template source name is required.",
                nameof(sourceName));
        }

        string safeName = Path.GetFileName(sourceName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new ArgumentException(
                "The template source name must include a file name.",
                nameof(sourceName));
        }

        SourceName = safeName;
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public string SourceName { get; }

    public DocumentModel Document { get; }
}

/// <summary>
/// Converts one supported format source into a deterministic rule package.
/// </summary>
public interface IFormatRequirementProvider
{
    string ProviderId { get; }

    bool CanHandle(FormatRequirementSource source);

    RulePackage Extract(FormatRequirementSource source);
}
