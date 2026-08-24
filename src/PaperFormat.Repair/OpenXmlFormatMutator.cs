using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using PaperFormat.Domain;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace PaperFormat.Repair;

internal sealed class OpenXmlFormatMutator
{
    private readonly MainDocumentPart _main;
    private readonly RulePackage _rules;
    private readonly OpenXmlLocationIndex _locations;

    public OpenXmlFormatMutator(
        MainDocumentPart main,
        RulePackage rules)
    {
        _main = main ?? throw new ArgumentNullException(nameof(main));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        W.Body body = main.Document?.Body
            ?? throw new InvalidDataException(
                "The main document part has no body.");
        _locations = new OpenXmlLocationIndex(body);
    }

    public bool Apply(
        FormatRule rule,
        StructuralLocation location)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(location);

        return rule.Target == RuleTarget.Page
            ? ApplyPage(rule.Property, rule.Expected, location)
            : ApplyContent(rule.Property, rule.Expected, location);
    }

    private bool ApplyPage(
        FormatProperty property,
        RuleValue value,
        StructuralLocation location)
    {
        W.SectionProperties? section =
            _locations.Find<W.SectionProperties>(location);
        if (section is null)
        {
            return false;
        }

        switch (property)
        {
            case FormatProperty.PageWidth:
                PageSize(section).Width = UnsignedTwip(value);
                return true;
            case FormatProperty.PageHeight:
                PageSize(section).Height = UnsignedTwip(value);
                return true;
            case FormatProperty.PageOrientation:
                PageSize(section).Orient =
                    ((PageOrientationRuleValue)value).Value
                        == PageOrientation.Landscape
                        ? W.PageOrientationValues.Landscape
                        : W.PageOrientationValues.Portrait;
                return true;
            case FormatProperty.MarginTop:
                PageMargin(section).Top = SignedTwip(value);
                return true;
            case FormatProperty.MarginRight:
                PageMargin(section).Right = UnsignedTwip(value);
                return true;
            case FormatProperty.MarginBottom:
                PageMargin(section).Bottom = SignedTwip(value);
                return true;
            case FormatProperty.MarginLeft:
                PageMargin(section).Left = UnsignedTwip(value);
                return true;
            default:
                return false;
        }
    }

    private bool ApplyContent(
        FormatProperty property,
        RuleValue value,
        StructuralLocation location)
    {
        if (IsCharacterProperty(property))
        {
            W.Run? run = _locations.Find<W.Run>(location);
            return run is not null
                && ApplyCharacter(RunProperties(run), property, value);
        }

        W.Paragraph? paragraph = _locations.Find<W.Paragraph>(location);
        if (paragraph is null)
        {
            return false;
        }

        W.ParagraphProperties properties = ParagraphProperties(paragraph);
        switch (property)
        {
            case FormatProperty.ParagraphStyleId:
                string styleId = ((TextRuleValue)value).Value;
                if (!EnsureParagraphStyle(styleId))
                {
                    return false;
                }

                W.ParagraphStyleId style =
                    GetOrAdd<W.ParagraphStyleId>(properties);
                style.Val = styleId;
                return true;
            case FormatProperty.ParagraphAlignment:
            case FormatProperty.LineSpacing:
            case FormatProperty.SpaceBefore:
            case FormatProperty.SpaceAfter:
            case FormatProperty.FirstLineIndent:
                return ApplyParagraph(properties, property, value);
            default:
                return false;
        }
    }

    private static bool ApplyCharacter(
        OpenXmlCompositeElement properties,
        FormatProperty property,
        RuleValue value)
    {
        switch (property)
        {
            case FormatProperty.FontAscii:
                RunFonts(properties).Ascii = ((TextRuleValue)value).Value;
                return true;
            case FormatProperty.FontHighAnsi:
                RunFonts(properties).HighAnsi = ((TextRuleValue)value).Value;
                return true;
            case FormatProperty.FontEastAsia:
                RunFonts(properties).EastAsia = ((TextRuleValue)value).Value;
                return true;
            case FormatProperty.FontComplexScript:
                RunFonts(properties).ComplexScript =
                    ((TextRuleValue)value).Value;
                return true;
            case FormatProperty.FontSize:
                string halfPoints = HalfPoints(value);
                W.FontSize size = GetOrAdd<W.FontSize>(properties);
                size.Val = halfPoints;
                W.FontSizeComplexScript complex =
                    GetOrAdd<W.FontSizeComplexScript>(properties);
                complex.Val = halfPoints;
                return true;
            case FormatProperty.Bold:
                W.Bold bold = GetOrAdd<W.Bold>(properties);
                bold.Val = ((BooleanRuleValue)value).Value;
                return true;
            case FormatProperty.Italic:
                W.Italic italic = GetOrAdd<W.Italic>(properties);
                italic.Val = ((BooleanRuleValue)value).Value;
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyParagraph(
        OpenXmlCompositeElement properties,
        FormatProperty property,
        RuleValue value)
    {
        switch (property)
        {
            case FormatProperty.ParagraphAlignment:
                W.Justification alignment =
                    GetOrAdd<W.Justification>(properties);
                alignment.Val = Alignment(
                    ((ParagraphAlignmentRuleValue)value).Value);
                return true;
            case FormatProperty.LineSpacing:
                ApplyLineSpacing(
                    Spacing(properties),
                    ((LineSpacingRuleValue)value).Value);
                return true;
            case FormatProperty.SpaceBefore:
                Spacing(properties).Before = TwipText(value);
                return true;
            case FormatProperty.SpaceAfter:
                Spacing(properties).After = TwipText(value);
                return true;
            case FormatProperty.FirstLineIndent:
                W.Indentation indentation =
                    GetOrAdd<W.Indentation>(properties);
                indentation.FirstLine = TwipText(value);
                indentation.Hanging = null;
                return true;
            default:
                return false;
        }
    }

    private bool EnsureParagraphStyle(string styleId)
    {
        if (StyleExists(styleId))
        {
            return true;
        }

        W.Styles styles = GetOrCreateStyles();
        RuleTarget[] targets = _rules.Rules
            .Where(rule => rule.Enabled && !rule.NeedsConfirmation)
            .Where(rule => rule.Property == FormatProperty.ParagraphStyleId)
            .Where(
                rule => rule.Expected is TextRuleValue expected
                    && string.Equals(
                        expected.Value,
                        styleId,
                        StringComparison.Ordinal))
            .Select(rule => rule.Target)
            .Distinct()
            .OrderBy(target => target)
            .ToArray();
        if (targets.Length == 0)
        {
            return false;
        }

        var style = new W.Style
        {
            Type = W.StyleValues.Paragraph,
            StyleId = styleId,
            CustomStyle = true,
        };
        style.AppendChild(new W.StyleName { Val = styleId });
        if (!string.Equals(styleId, "Normal", StringComparison.Ordinal)
            && StyleExists("Normal"))
        {
            style.AppendChild(new W.BasedOn { Val = "Normal" });
        }

        var paragraphProperties = new W.StyleParagraphProperties();
        foreach (FormatProperty property in ParagraphStyleProperties)
        {
            RuleValue? value = ConsensusValue(targets, property);
            if (value is not null)
            {
                ApplyParagraph(paragraphProperties, property, value);
            }
        }

        if (paragraphProperties.HasChildren)
        {
            style.AppendChild(paragraphProperties);
        }

        var runProperties = new W.StyleRunProperties();
        foreach (FormatProperty property in CharacterStyleProperties)
        {
            RuleValue? value = ConsensusValue(targets, property);
            if (value is not null)
            {
                ApplyCharacter(runProperties, property, value);
            }
        }

        if (runProperties.HasChildren)
        {
            style.AppendChild(runProperties);
        }

        styles.AddChild(style, throwOnError: true);
        return true;
    }

    private RuleValue? ConsensusValue(
        IReadOnlyCollection<RuleTarget> targets,
        FormatProperty property)
    {
        List<RuleValue> values = [];
        foreach (RuleTarget target in targets)
        {
            RuleValue[] targetValues = _rules.Rules
                .Where(rule => rule.Enabled && !rule.NeedsConfirmation)
                .Where(rule => rule.Target == target)
                .Where(rule => rule.Property == property)
                .Select(rule => rule.Expected)
                .Distinct()
                .ToArray();
            if (targetValues.Length != 1)
            {
                return null;
            }

            values.Add(targetValues[0]);
        }

        return values.Distinct().Count() == 1 ? values[0] : null;
    }

    private W.Styles GetOrCreateStyles()
    {
        StyleDefinitionsPart part = _main.StyleDefinitionsPart
            ?? _main.AddNewPart<StyleDefinitionsPart>();
        return part.Styles ??= new W.Styles();
    }

    private bool StyleExists(string styleId) =>
        _main.StyleDefinitionsPart?.Styles?
            .Elements<W.Style>()
            .Any(
                style => string.Equals(
                    style.StyleId?.Value,
                    styleId,
                    StringComparison.Ordinal))
        == true;

    private static W.PageSize PageSize(W.SectionProperties properties) =>
        GetOrAdd<W.PageSize>(properties);

    private static W.PageMargin PageMargin(W.SectionProperties properties) =>
        GetOrAdd<W.PageMargin>(properties);

    private static W.ParagraphProperties ParagraphProperties(
        W.Paragraph paragraph) =>
        paragraph.ParagraphProperties
        ?? paragraph.PrependChild(new W.ParagraphProperties());

    private static W.RunProperties RunProperties(W.Run run) =>
        run.RunProperties
        ?? run.PrependChild(new W.RunProperties());

    private static W.RunFonts RunFonts(OpenXmlCompositeElement properties) =>
        GetOrAdd<W.RunFonts>(properties);

    private static W.SpacingBetweenLines Spacing(
        OpenXmlCompositeElement properties) =>
        GetOrAdd<W.SpacingBetweenLines>(properties);

    private static T GetOrAdd<T>(OpenXmlCompositeElement parent)
        where T : OpenXmlElement, new()
    {
        T? existing = parent.GetFirstChild<T>();
        if (existing is not null)
        {
            return existing;
        }

        var created = new T();
        parent.AddChild(created, throwOnError: true);
        return created;
    }

    private static void ApplyLineSpacing(
        W.SpacingBetweenLines target,
        LineSpacing value)
    {
        if (value.Kind == LineSpacingKind.Auto)
        {
            target.Line = value.Multiple!.Value.Value
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            target.LineRule = W.LineSpacingRuleValues.Auto;
            return;
        }

        target.Line = value.Length!.Value.Value
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        target.LineRule = value.Kind == LineSpacingKind.Exact
            ? W.LineSpacingRuleValues.Exact
            : W.LineSpacingRuleValues.AtLeast;
    }

    private static W.JustificationValues Alignment(
        ParagraphAlignment value) =>
        value switch
        {
            ParagraphAlignment.Left => W.JustificationValues.Left,
            ParagraphAlignment.Center => W.JustificationValues.Center,
            ParagraphAlignment.Right => W.JustificationValues.Right,
            ParagraphAlignment.Justified => W.JustificationValues.Both,
            ParagraphAlignment.Distributed => W.JustificationValues.Distribute,
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                null),
        };

    private static UInt32Value UnsignedTwip(RuleValue value)
    {
        int twips = checked(
            (int)((TwipRuleValue)value).Value.Value);
        return checked((uint)twips);
    }

    private static Int32Value SignedTwip(RuleValue value) =>
        new(checked((int)((TwipRuleValue)value).Value.Value));

    private static string TwipText(RuleValue value) =>
        ((TwipRuleValue)value).Value.Value.ToString(
            System.Globalization.CultureInfo.InvariantCulture);

    private static string HalfPoints(RuleValue value)
    {
        int twips = checked(
            (int)((TwipRuleValue)value).Value.Value);
        if (twips % 10 != 0)
        {
            throw new InvalidOperationException(
                "Word font sizes must be representable in half points.");
        }

        return (twips / 10).ToString(
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsCharacterProperty(FormatProperty property) =>
        property is
            FormatProperty.FontAscii
            or FormatProperty.FontHighAnsi
            or FormatProperty.FontEastAsia
            or FormatProperty.FontComplexScript
            or FormatProperty.FontSize
            or FormatProperty.Bold
            or FormatProperty.Italic;

    private static readonly FormatProperty[] ParagraphStyleProperties =
    [
        FormatProperty.ParagraphAlignment,
        FormatProperty.LineSpacing,
        FormatProperty.SpaceBefore,
        FormatProperty.SpaceAfter,
        FormatProperty.FirstLineIndent,
    ];

    private static readonly FormatProperty[] CharacterStyleProperties =
    [
        FormatProperty.FontAscii,
        FormatProperty.FontHighAnsi,
        FormatProperty.FontEastAsia,
        FormatProperty.FontComplexScript,
        FormatProperty.FontSize,
        FormatProperty.Bold,
        FormatProperty.Italic,
    ];
}
