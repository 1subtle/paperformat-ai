using System.Text.Json.Serialization;

namespace PaperFormat.Domain;

/// <summary>
/// Manuscript or page element targeted by a formatting rule.
/// </summary>
public enum RuleTarget
{
    Page,
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
/// Normalized document property constrained by a formatting rule.
/// </summary>
public enum FormatProperty
{
    PageWidth,
    PageHeight,
    PageOrientation,
    MarginTop,
    MarginRight,
    MarginBottom,
    MarginLeft,
    ColumnCount,
    ColumnSpacing,
    FontAscii,
    FontHighAnsi,
    FontEastAsia,
    FontComplexScript,
    FontSize,
    Bold,
    Italic,
    ParagraphAlignment,
    LineSpacing,
    SpaceBefore,
    SpaceAfter,
    FirstLineIndent,
    ParagraphStyleId,
    CaptionNumberSequence,
    DirectFormattingConsistency,
}

/// <summary>
/// Default importance of a failed formatting rule.
/// </summary>
public enum RuleSeverity
{
    Error,
    Warning,
    Information,
}

/// <summary>
/// Repair policy attached to a formatting rule.
/// </summary>
public enum RepairLevel
{
    None,
    Safe,
    RequiresConfirmation,
}

/// <summary>
/// Origin of evidence used to create a formatting rule.
/// </summary>
public enum RuleEvidenceKind
{
    BuiltIn,
    TemplateSection,
    TemplateSample,
    TemplateStyle,
    UserOverride,
}

/// <summary>
/// Severity of a rule-extraction notice.
/// </summary>
public enum RuleNoticeSeverity
{
    Warning,
    Information,
}

/// <summary>
/// A typed expected value used by a formatting rule.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TwipRuleValue), "twip")]
[JsonDerivedType(typeof(IntegerRuleValue), "integer")]
[JsonDerivedType(typeof(BooleanRuleValue), "boolean")]
[JsonDerivedType(typeof(TextRuleValue), "text")]
[JsonDerivedType(typeof(PageOrientationRuleValue), "pageOrientation")]
[JsonDerivedType(
    typeof(ParagraphAlignmentRuleValue),
    "paragraphAlignment")]
[JsonDerivedType(typeof(LineSpacingRuleValue), "lineSpacing")]
public abstract record RuleValue;

/// <summary>
/// A rule value measured in twentieths of a point.
/// </summary>
public sealed record TwipRuleValue(Twip Value) : RuleValue;

/// <summary>
/// An integer rule value.
/// </summary>
public sealed record IntegerRuleValue(int Value) : RuleValue;

/// <summary>
/// A Boolean rule value.
/// </summary>
public sealed record BooleanRuleValue(bool Value) : RuleValue;

/// <summary>
/// A non-blank text rule value.
/// </summary>
public sealed record TextRuleValue : RuleValue
{
    public TextRuleValue(string value)
    {
        Value = DomainGuard.RequiredIdentifier(value, nameof(value));
    }

    public string Value { get; }
}

/// <summary>
/// A page-orientation rule value.
/// </summary>
public sealed record PageOrientationRuleValue(PageOrientation Value)
    : RuleValue;

/// <summary>
/// A paragraph-alignment rule value.
/// </summary>
public sealed record ParagraphAlignmentRuleValue(ParagraphAlignment Value)
    : RuleValue;

/// <summary>
/// A line-spacing rule value.
/// </summary>
public sealed record LineSpacingRuleValue : RuleValue
{
    public LineSpacingRuleValue(LineSpacing value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public LineSpacing Value { get; }
}

/// <summary>
/// Source evidence for an extracted or built-in rule.
/// </summary>
public sealed record RuleEvidence
{
    public RuleEvidence(
        RuleEvidenceKind kind,
        string providerId,
        string reference)
    {
        Kind = kind;
        ProviderId = DomainGuard.RequiredIdentifier(
            providerId,
            nameof(providerId));
        Reference = DomainGuard.RequiredIdentifier(
            reference,
            nameof(reference));
    }

    public RuleEvidenceKind Kind { get; }

    public string ProviderId { get; }

    public string Reference { get; }
}

/// <summary>
/// One independently checkable formatting requirement.
/// </summary>
public sealed record FormatRule
{
    public FormatRule(
        string ruleId,
        RuleTarget target,
        FormatProperty property,
        RuleValue expected,
        RuleSeverity severity,
        RepairLevel repairLevel,
        RuleEvidence evidence,
        decimal confidence,
        bool enabled = true,
        bool needsConfirmation = false)
    {
        RuleId = DomainGuard.RequiredIdentifier(ruleId, nameof(ruleId));
        Target = target;
        Property = property;
        Expected = expected ?? throw new ArgumentNullException(nameof(expected));
        ValidateExpectedValue(property, expected);
        Severity = severity;
        RepairLevel = repairLevel;
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));

        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                confidence,
                "Rule confidence must be between zero and one.");
        }

        Confidence = confidence;
        Enabled = enabled;
        NeedsConfirmation = needsConfirmation;
    }

    public string RuleId { get; }

    public RuleTarget Target { get; }

    public FormatProperty Property { get; }

    public RuleValue Expected { get; }

    public RuleSeverity Severity { get; }

    public RepairLevel RepairLevel { get; }

    public RuleEvidence Evidence { get; }

    public decimal Confidence { get; }

    public bool Enabled { get; }

    public bool NeedsConfirmation { get; }

    private static void ValidateExpectedValue(
        FormatProperty property,
        RuleValue value)
    {
        bool isValid = property switch
        {
            FormatProperty.PageWidth
                or FormatProperty.PageHeight
                or FormatProperty.MarginTop
                or FormatProperty.MarginRight
                or FormatProperty.MarginBottom
                or FormatProperty.MarginLeft
                or FormatProperty.ColumnSpacing
                or FormatProperty.FontSize
                or FormatProperty.SpaceBefore
                or FormatProperty.SpaceAfter
                or FormatProperty.FirstLineIndent =>
                value is TwipRuleValue,
            FormatProperty.ColumnCount => value is IntegerRuleValue,
            FormatProperty.PageOrientation =>
                value is PageOrientationRuleValue,
            FormatProperty.FontAscii
                or FormatProperty.FontHighAnsi
                or FormatProperty.FontEastAsia
                or FormatProperty.FontComplexScript
                or FormatProperty.ParagraphStyleId =>
                value is TextRuleValue,
            FormatProperty.Bold
                or FormatProperty.Italic
                or FormatProperty.CaptionNumberSequence
                or FormatProperty.DirectFormattingConsistency =>
                value is BooleanRuleValue,
            FormatProperty.ParagraphAlignment =>
                value is ParagraphAlignmentRuleValue,
            FormatProperty.LineSpacing => value is LineSpacingRuleValue,
            _ => false,
        };

        if (!isValid)
        {
            throw new ArgumentException(
                $"The value type is not valid for property '{property}'.",
                nameof(value));
        }
    }
}

/// <summary>
/// A safe, non-content-bearing notice produced during rule extraction.
/// </summary>
public sealed record RulePackageNotice
{
    public RulePackageNotice(
        string code,
        RuleNoticeSeverity severity,
        string message,
        string reference)
    {
        Code = DomainGuard.RequiredIdentifier(code, nameof(code));
        Severity = severity;
        Message = DomainGuard.RequiredIdentifier(message, nameof(message));
        Reference = DomainGuard.RequiredIdentifier(
            reference,
            nameof(reference));
    }

    public string Code { get; }

    public RuleNoticeSeverity Severity { get; }

    public string Message { get; }

    public string Reference { get; }
}

/// <summary>
/// A deterministic, versioned collection of formatting rules.
/// </summary>
public sealed record RulePackage
{
    public const string CurrentSchemaVersion = "1.0";

    public RulePackage(
        string packageId,
        int revision,
        string name,
        string providerId,
        string providerVersion,
        string sourceReference,
        IEnumerable<FormatRule> rules,
        IEnumerable<RulePackageNotice>? notices = null,
        string schemaVersion = CurrentSchemaVersion)
    {
        PackageId = DomainGuard.RequiredIdentifier(
            packageId,
            nameof(packageId));
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision),
                revision,
                "A rule package revision must be positive.");
        }

        Revision = revision;
        Name = DomainGuard.RequiredIdentifier(name, nameof(name));
        ProviderId = DomainGuard.RequiredIdentifier(
            providerId,
            nameof(providerId));
        ProviderVersion = DomainGuard.RequiredIdentifier(
            providerVersion,
            nameof(providerVersion));
        SourceReference = DomainGuard.RequiredIdentifier(
            sourceReference,
            nameof(sourceReference));
        SchemaVersion = DomainGuard.RequiredIdentifier(
            schemaVersion,
            nameof(schemaVersion));

        FormatRule[] orderedRules = (
            rules ?? throw new ArgumentNullException(nameof(rules)))
            .OrderBy(rule => rule.RuleId, StringComparer.Ordinal)
            .ToArray();
        string? duplicateRuleId = orderedRules
            .GroupBy(rule => rule.RuleId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateRuleId is not null)
        {
            throw new ArgumentException(
                "A rule package cannot contain duplicate rule identifiers.",
                nameof(rules));
        }

        Rules = new ValueList<FormatRule>(orderedRules);
        Notices = new ValueList<RulePackageNotice>(
            (notices ?? Array.Empty<RulePackageNotice>())
            .OrderBy(notice => notice.Code, StringComparer.Ordinal)
            .ThenBy(notice => notice.Reference, StringComparer.Ordinal));
    }

    public string SchemaVersion { get; }

    public string PackageId { get; }

    public int Revision { get; }

    public string Name { get; }

    public string ProviderId { get; }

    public string ProviderVersion { get; }

    public string SourceReference { get; }

    public ValueList<FormatRule> Rules { get; }

    public ValueList<RulePackageNotice> Notices { get; }
}

/// <summary>
/// A user-confirmed edit to one rule in a rule package.
/// </summary>
public sealed record RuleOverride
{
    public RuleOverride(
        string ruleId,
        RuleValue? expected = null,
        bool? enabled = null)
    {
        RuleId = DomainGuard.RequiredIdentifier(ruleId, nameof(ruleId));
        if (expected is null && enabled is null)
        {
            throw new ArgumentException(
                "A rule override must change a value or enabled state.");
        }

        Expected = expected;
        Enabled = enabled;
    }

    public string RuleId { get; }

    public RuleValue? Expected { get; }

    public bool? Enabled { get; }
}
