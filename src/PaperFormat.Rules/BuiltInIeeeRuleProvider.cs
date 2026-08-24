using PaperFormat.Domain;

namespace PaperFormat.Rules;

/// <summary>
/// Deterministic IEEE-like profile used by the MVP demo and regression suite.
/// </summary>
public sealed class BuiltInIeeeRuleProvider : IFormatRequirementProvider
{
    public const string ProfileId = "ieee-like-v1";
    private const string Version = "1.0.0";

    public string ProviderId => "builtin.ieee-like";

    public bool CanHandle(FormatRequirementSource source) =>
        source is BuiltInFormatRequirementSource builtIn
        && string.Equals(
            builtIn.ProfileId,
            ProfileId,
            StringComparison.Ordinal);

    public RulePackage Extract(FormatRequirementSource source)
    {
        if (!CanHandle(source))
        {
            throw new ArgumentException(
                $"Provider '{ProviderId}' only supports profile '{ProfileId}'.",
                nameof(source));
        }

        List<FormatRule> rules = [];
        AddPageRules(rules);
        AddTextRules(
            rules,
            RuleTarget.Title,
            "PaperTitle",
            24m,
            ParagraphAlignment.Center,
            bold: false,
            italic: false,
            before: 0,
            after: 120);
        AddTextRules(
            rules,
            RuleTarget.Author,
            "Authors",
            11m,
            ParagraphAlignment.Center,
            bold: false,
            italic: false,
            before: 0,
            after: 60);
        AddTextRules(
            rules,
            RuleTarget.Affiliation,
            "Affiliation",
            9m,
            ParagraphAlignment.Center,
            bold: false,
            italic: true,
            before: 0,
            after: 120);
        AddTextRules(
            rules,
            RuleTarget.Abstract,
            "AbstractText",
            9m,
            ParagraphAlignment.Justified,
            bold: false,
            italic: false,
            before: 0,
            after: 60);
        AddTextRules(
            rules,
            RuleTarget.Keywords,
            "Keywords",
            9m,
            ParagraphAlignment.Justified,
            bold: false,
            italic: false,
            before: 0,
            after: 120);
        AddTextRules(
            rules,
            RuleTarget.Heading1,
            "Heading1",
            10m,
            ParagraphAlignment.Center,
            bold: true,
            italic: false,
            before: 120,
            after: 60);
        AddTextRules(
            rules,
            RuleTarget.Heading2,
            "Heading2",
            10m,
            ParagraphAlignment.Left,
            bold: false,
            italic: true,
            before: 120,
            after: 40);
        AddTextRules(
            rules,
            RuleTarget.Heading3,
            "Heading3",
            10m,
            ParagraphAlignment.Left,
            bold: false,
            italic: true,
            before: 80,
            after: 40);
        AddTextRules(
            rules,
            RuleTarget.Body,
            "BodyText",
            10m,
            ParagraphAlignment.Justified,
            bold: false,
            italic: false,
            before: 0,
            after: 0,
            firstLineIndent: 289);
        AddTextRules(
            rules,
            RuleTarget.FigureCaption,
            "Caption",
            8m,
            ParagraphAlignment.Center,
            bold: false,
            italic: false,
            before: 40,
            after: 80);
        AddTextRules(
            rules,
            RuleTarget.TableCaption,
            "Caption",
            8m,
            ParagraphAlignment.Center,
            bold: false,
            italic: false,
            before: 40,
            after: 80);
        AddTextRules(
            rules,
            RuleTarget.TableText,
            "TableText",
            8m,
            ParagraphAlignment.Left,
            bold: false,
            italic: false,
            before: 0,
            after: 0,
            includeEmphasisRules: false);
        AddTextRules(
            rules,
            RuleTarget.ReferencesHeading,
            "Heading1",
            10m,
            ParagraphAlignment.Center,
            bold: true,
            italic: false,
            before: 120,
            after: 60);
        AddTextRules(
            rules,
            RuleTarget.ReferenceEntry,
            "BodyText",
            8m,
            ParagraphAlignment.Justified,
            bold: false,
            italic: false,
            before: 0,
            after: 0);

        AddRule(
            rules,
            RuleTarget.Body,
            FormatProperty.DirectFormattingConsistency,
            new BooleanRuleValue(true),
            RuleSeverity.Warning,
            RepairLevel.None);
        AddRule(
            rules,
            RuleTarget.FigureCaption,
            FormatProperty.CaptionNumberSequence,
            new BooleanRuleValue(true),
            RuleSeverity.Warning,
            RepairLevel.None);
        AddRule(
            rules,
            RuleTarget.TableCaption,
            FormatProperty.CaptionNumberSequence,
            new BooleanRuleValue(true),
            RuleSeverity.Warning,
            RepairLevel.None);

        return new RulePackage(
            ProfileId,
            revision: 1,
            "IEEE-like MVP profile",
            ProviderId,
            Version,
            $"profile:{ProfileId}",
            rules);
    }

    private void AddPageRules(ICollection<FormatRule> rules)
    {
        AddRule(
            rules,
            RuleTarget.Page,
            FormatProperty.PageWidth,
            new TwipRuleValue(new Twip(12_240)),
            RuleSeverity.Error,
            RepairLevel.RequiresConfirmation);
        AddRule(
            rules,
            RuleTarget.Page,
            FormatProperty.PageHeight,
            new TwipRuleValue(new Twip(15_840)),
            RuleSeverity.Error,
            RepairLevel.RequiresConfirmation);
        AddRule(
            rules,
            RuleTarget.Page,
            FormatProperty.PageOrientation,
            new PageOrientationRuleValue(PageOrientation.Portrait),
            RuleSeverity.Error,
            RepairLevel.RequiresConfirmation);
        AddRule(
            rules,
            RuleTarget.Page,
            FormatProperty.MarginTop,
            new TwipRuleValue(new Twip(1_080)),
            RuleSeverity.Error,
            RepairLevel.RequiresConfirmation);
        AddRule(
            rules,
            RuleTarget.Page,
            FormatProperty.MarginRight,
            new TwipRuleValue(new Twip(720)),
            RuleSeverity.Error,
            RepairLevel.RequiresConfirmation);
        AddRule(
            rules,
            RuleTarget.Page,
            FormatProperty.MarginBottom,
            new TwipRuleValue(new Twip(1_080)),
            RuleSeverity.Error,
            RepairLevel.RequiresConfirmation);
        AddRule(
            rules,
            RuleTarget.Page,
            FormatProperty.MarginLeft,
            new TwipRuleValue(new Twip(720)),
            RuleSeverity.Error,
            RepairLevel.RequiresConfirmation);
        AddRule(
            rules,
            RuleTarget.Page,
            FormatProperty.ColumnCount,
            new IntegerRuleValue(2),
            RuleSeverity.Error,
            RepairLevel.None);
        AddRule(
            rules,
            RuleTarget.Page,
            FormatProperty.ColumnSpacing,
            new TwipRuleValue(new Twip(360)),
            RuleSeverity.Error,
            RepairLevel.None);
    }

    private void AddTextRules(
        ICollection<FormatRule> rules,
        RuleTarget target,
        string styleId,
        decimal fontSizePoints,
        ParagraphAlignment alignment,
        bool bold,
        bool italic,
        int before,
        int after,
        bool includeEmphasisRules = true,
        int? firstLineIndent = null)
    {
        AddRule(
            rules,
            target,
            FormatProperty.ParagraphStyleId,
            new TextRuleValue(styleId),
            RuleSeverity.Warning,
            RepairLevel.None);
        AddRule(
            rules,
            target,
            FormatProperty.FontAscii,
            new TextRuleValue("Times New Roman"),
            RuleSeverity.Warning,
            RepairLevel.Safe);
        AddRule(
            rules,
            target,
            FormatProperty.FontHighAnsi,
            new TextRuleValue("Times New Roman"),
            RuleSeverity.Warning,
            RepairLevel.Safe);
        AddRule(
            rules,
            target,
            FormatProperty.FontEastAsia,
            new TextRuleValue("Times New Roman"),
            RuleSeverity.Warning,
            RepairLevel.Safe);
        AddRule(
            rules,
            target,
            FormatProperty.FontComplexScript,
            new TextRuleValue("Times New Roman"),
            RuleSeverity.Warning,
            RepairLevel.Safe);
        AddRule(
            rules,
            target,
            FormatProperty.FontSize,
            new TwipRuleValue(Twip.FromPoints(fontSizePoints)),
            RuleSeverity.Warning,
            RepairLevel.Safe);
        if (includeEmphasisRules)
        {
            AddRule(
                rules,
                target,
                FormatProperty.Bold,
                new BooleanRuleValue(bold),
                RuleSeverity.Warning,
                RepairLevel.Safe);
            AddRule(
                rules,
                target,
                FormatProperty.Italic,
                new BooleanRuleValue(italic),
                RuleSeverity.Warning,
                RepairLevel.Safe);
        }
        AddRule(
            rules,
            target,
            FormatProperty.ParagraphAlignment,
            new ParagraphAlignmentRuleValue(alignment),
            RuleSeverity.Warning,
            RepairLevel.Safe);
        AddRule(
            rules,
            target,
            FormatProperty.LineSpacing,
            new LineSpacingRuleValue(
                LineSpacing.Automatic(new LineMultiple(240))),
            RuleSeverity.Warning,
            RepairLevel.Safe);
        AddRule(
            rules,
            target,
            FormatProperty.SpaceBefore,
            new TwipRuleValue(new Twip(before)),
            RuleSeverity.Warning,
            RepairLevel.Safe);
        AddRule(
            rules,
            target,
            FormatProperty.SpaceAfter,
            new TwipRuleValue(new Twip(after)),
            RuleSeverity.Warning,
            RepairLevel.Safe);
        if (firstLineIndent is not null)
        {
            AddRule(
                rules,
                target,
                FormatProperty.FirstLineIndent,
                new TwipRuleValue(new Twip(firstLineIndent.Value)),
                RuleSeverity.Warning,
                RepairLevel.Safe);
        }
    }

    private void AddRule(
        ICollection<FormatRule> rules,
        RuleTarget target,
        FormatProperty property,
        RuleValue expected,
        RuleSeverity severity,
        RepairLevel repairLevel)
    {
        rules.Add(new FormatRule(
            $"{ProfileId}.{TargetId(target)}.{PropertyId(property)}",
            target,
            property,
            expected,
            severity,
            repairLevel,
            new RuleEvidence(
                RuleEvidenceKind.BuiltIn,
                ProviderId,
                $"profile:{ProfileId}"),
            confidence: 1m));
    }

    private static string TargetId(RuleTarget target) =>
        target switch
        {
            RuleTarget.Page => "page",
            RuleTarget.Title => "title",
            RuleTarget.Author => "author",
            RuleTarget.Affiliation => "affiliation",
            RuleTarget.Abstract => "abstract",
            RuleTarget.Keywords => "keywords",
            RuleTarget.Heading1 => "heading-1",
            RuleTarget.Heading2 => "heading-2",
            RuleTarget.Heading3 => "heading-3",
            RuleTarget.Body => "body",
            RuleTarget.FigureCaption => "figure-caption",
            RuleTarget.TableCaption => "table-caption",
            RuleTarget.TableText => "table-text",
            RuleTarget.ReferencesHeading => "references-heading",
            RuleTarget.ReferenceEntry => "reference-entry",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    private static string PropertyId(FormatProperty property) =>
        property switch
        {
            FormatProperty.PageWidth => "page-width",
            FormatProperty.PageHeight => "page-height",
            FormatProperty.PageOrientation => "page-orientation",
            FormatProperty.MarginTop => "margin-top",
            FormatProperty.MarginRight => "margin-right",
            FormatProperty.MarginBottom => "margin-bottom",
            FormatProperty.MarginLeft => "margin-left",
            FormatProperty.ColumnCount => "column-count",
            FormatProperty.ColumnSpacing => "column-spacing",
            FormatProperty.FontAscii => "font-ascii",
            FormatProperty.FontHighAnsi => "font-high-ansi",
            FormatProperty.FontEastAsia => "font-east-asia",
            FormatProperty.FontComplexScript => "font-complex-script",
            FormatProperty.FontSize => "font-size",
            FormatProperty.Bold => "bold",
            FormatProperty.Italic => "italic",
            FormatProperty.ParagraphAlignment => "paragraph-alignment",
            FormatProperty.LineSpacing => "line-spacing",
            FormatProperty.SpaceBefore => "space-before",
            FormatProperty.SpaceAfter => "space-after",
            FormatProperty.FirstLineIndent => "first-line-indent",
            FormatProperty.ParagraphStyleId => "paragraph-style-id",
            FormatProperty.CaptionNumberSequence => "caption-number-sequence",
            FormatProperty.DirectFormattingConsistency =>
                "direct-formatting-consistency",
            _ => throw new ArgumentOutOfRangeException(
                nameof(property),
                property,
                null),
        };
}
