namespace PaperFormat.Domain;

/// <summary>
/// Logical manuscript element recognized by the MVP classifier.
/// </summary>
public enum ManuscriptElementKind
{
    Unclassified,
    Title,
    Author,
    Affiliation,
    Abstract,
    Keywords,
    Heading1,
    Heading2,
    Heading3,
    Body,
    FigureCaption,
    TableCaption,
    TableText,
    ReferencesHeading,
    ReferenceEntry,
}

/// <summary>
/// Review state of a manuscript element classification.
/// </summary>
public enum ClassificationStatus
{
    Confirmed,
    NeedsConfirmation,
    Unclassified,
    UserConfirmed,
}

/// <summary>
/// Independent evidence category used by the deterministic classifier.
/// </summary>
public enum ClassificationEvidenceKind
{
    Style,
    TextPattern,
    Formatting,
    Position,
    Context,
    TableStructure,
    UserOverride,
}

/// <summary>
/// A content-safe reason contributing to one classification.
/// </summary>
public sealed record ClassificationReason
{
    public ClassificationReason(
        string code,
        ClassificationEvidenceKind evidenceKind,
        decimal weight,
        string description)
    {
        Code = DomainGuard.RequiredIdentifier(code, nameof(code));
        EvidenceKind = evidenceKind;
        if (weight is <= 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weight),
                weight,
                "A classification weight must be greater than zero and at most one.");
        }

        Weight = weight;
        Description = DomainGuard.RequiredIdentifier(
            description,
            nameof(description));
    }

    public string Code { get; }

    public ClassificationEvidenceKind EvidenceKind { get; }

    public decimal Weight { get; }

    public string Description { get; }
}

/// <summary>
/// Classification of one paragraph-like document element.
/// </summary>
public sealed record DocumentElement
{
    public DocumentElement(
        string elementId,
        StructuralLocation location,
        ManuscriptElementKind kind,
        decimal confidence,
        ClassificationStatus status,
        IEnumerable<ClassificationReason> reasons,
        int textLength,
        string? sourceStyleId)
    {
        ElementId = DomainGuard.RequiredIdentifier(
            elementId,
            nameof(elementId));
        Location = location ?? throw new ArgumentNullException(nameof(location));
        if (Location.ParagraphIndex is null)
        {
            throw new ArgumentException(
                "A classified element requires a paragraph location.",
                nameof(location));
        }

        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                confidence,
                "Classification confidence must be between zero and one.");
        }

        if (textLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(textLength),
                textLength,
                "Text length cannot be negative.");
        }

        if ((kind == ManuscriptElementKind.Unclassified)
            != (status == ClassificationStatus.Unclassified))
        {
            throw new ArgumentException(
                "Unclassified kind and status must be used together.");
        }

        Kind = kind;
        Confidence = confidence;
        Status = status;
        Reasons = new ValueList<ClassificationReason>(
            (reasons ?? throw new ArgumentNullException(nameof(reasons)))
            .OrderBy(reason => reason.Code, StringComparer.Ordinal));
        TextLength = textLength;
        SourceStyleId = DomainGuard.OptionalNonBlank(
            sourceStyleId,
            nameof(sourceStyleId));
    }

    public string ElementId { get; }

    public StructuralLocation Location { get; }

    public ManuscriptElementKind Kind { get; }

    public decimal Confidence { get; }

    public ClassificationStatus Status { get; }

    public ValueList<ClassificationReason> Reasons { get; }

    public int TextLength { get; }

    public string? SourceStyleId { get; }
}

/// <summary>
/// Immutable classifications for one parsed document revision.
/// </summary>
public sealed record ClassificationSet
{
    public ClassificationSet(
        int revision,
        IEnumerable<DocumentElement> elements)
    {
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision),
                revision,
                "A classification revision must be positive.");
        }

        Revision = revision;
        DocumentElement[] ordered = (
            elements ?? throw new ArgumentNullException(nameof(elements)))
            .OrderBy(element => element.Location)
            .ToArray();
        string? duplicateId = ordered
            .GroupBy(element => element.ElementId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                "A classification set cannot contain duplicate element identifiers.",
                nameof(elements));
        }

        Elements = new ValueList<DocumentElement>(ordered);
    }

    public int Revision { get; }

    public ValueList<DocumentElement> Elements { get; }
}

/// <summary>
/// A user-confirmed classification for one existing element.
/// </summary>
public sealed record ClassificationOverride
{
    public ClassificationOverride(
        string elementId,
        ManuscriptElementKind kind)
    {
        ElementId = DomainGuard.RequiredIdentifier(
            elementId,
            nameof(elementId));
        if (kind == ManuscriptElementKind.Unclassified)
        {
            throw new ArgumentException(
                "A user override must select a concrete element kind.",
                nameof(kind));
        }

        Kind = kind;
    }

    public string ElementId { get; }

    public ManuscriptElementKind Kind { get; }
}
