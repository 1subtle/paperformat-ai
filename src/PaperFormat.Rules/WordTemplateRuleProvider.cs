using System.Security.Cryptography;
using System.Text;
using PaperFormat.Domain;

namespace PaperFormat.Rules;

/// <summary>
/// Extracts deterministic rules from page settings and styled template samples.
/// </summary>
public sealed class WordTemplateRuleProvider : IFormatRequirementProvider
{
    private const string Version = "1.0.0";

    public string ProviderId => "template.word";

    public bool CanHandle(FormatRequirementSource source) =>
        source is WordTemplateFormatRequirementSource;

    public RulePackage Extract(FormatRequirementSource source)
    {
        if (source is not WordTemplateFormatRequirementSource template)
        {
            throw new ArgumentException(
                $"Provider '{ProviderId}' requires a parsed Word template.",
                nameof(source));
        }

        List<FormatRule> rules = [];
        List<RulePackageNotice> notices = [];
        ExtractPageRules(template.Document, rules, notices);
        ExtractTextRules(template.Document, rules, notices);

        return new RulePackage(
            PackageId(template),
            revision: 1,
            $"Extracted rules — {template.SourceName}",
            ProviderId,
            Version,
            $"template:{template.SourceName}",
            rules,
            notices);
    }

    private void ExtractPageRules(
        DocumentModel document,
        ICollection<FormatRule> rules,
        List<RulePackageNotice> notices)
    {
        AddUniformPageRule(
            document,
            rules,
            notices,
            FormatProperty.PageWidth,
            section => section.PageSettings.Width,
            value => new TwipRuleValue(value));
        AddUniformPageRule(
            document,
            rules,
            notices,
            FormatProperty.PageHeight,
            section => section.PageSettings.Height,
            value => new TwipRuleValue(value));
        AddUniformPageRule(
            document,
            rules,
            notices,
            FormatProperty.PageOrientation,
            section => section.PageSettings.Orientation,
            value => new PageOrientationRuleValue(value));
        AddUniformPageRule(
            document,
            rules,
            notices,
            FormatProperty.MarginTop,
            section => section.PageSettings.Margins.Top,
            value => new TwipRuleValue(value));
        AddUniformPageRule(
            document,
            rules,
            notices,
            FormatProperty.MarginRight,
            section => section.PageSettings.Margins.Right,
            value => new TwipRuleValue(value));
        AddUniformPageRule(
            document,
            rules,
            notices,
            FormatProperty.MarginBottom,
            section => section.PageSettings.Margins.Bottom,
            value => new TwipRuleValue(value));
        AddUniformPageRule(
            document,
            rules,
            notices,
            FormatProperty.MarginLeft,
            section => section.PageSettings.Margins.Left,
            value => new TwipRuleValue(value));
        AddUniformPageRule(
            document,
            rules,
            notices,
            FormatProperty.ColumnCount,
            section => (int?)section.PageSettings.Columns.Count,
            value => new IntegerRuleValue(value));
        AddUniformPageRule(
            document,
            rules,
            notices,
            FormatProperty.ColumnSpacing,
            section => section.PageSettings.Columns.Spacing,
            value => new TwipRuleValue(value));
    }

    private void AddUniformPageRule<T>(
        DocumentModel document,
        ICollection<FormatRule> rules,
        List<RulePackageNotice> notices,
        FormatProperty property,
        Func<SectionModel, T?> selector,
        Func<T, RuleValue> valueFactory)
        where T : struct
    {
        T?[] values = document.Sections.Select(selector).ToArray();
        T[] known = values
            .Where(value => value.HasValue)
            .Select(value => value.GetValueOrDefault())
            .Distinct()
            .ToArray();
        string reference = "sections:all";

        if (known.Length == 0)
        {
            notices.Add(new RulePackageNotice(
                $"template.page.{PropertyId(property)}.missing",
                RuleNoticeSeverity.Warning,
                "The template does not define this page property.",
                reference));
            return;
        }

        if (known.Length > 1)
        {
            notices.Add(new RulePackageNotice(
                $"template.page.{PropertyId(property)}.ambiguous",
                RuleNoticeSeverity.Warning,
                "The template uses multiple values for this page property.",
                reference));
            return;
        }

        bool hasUnknownSection = values.Any(value => !value.HasValue);
        AddRule(
            rules,
            RuleTarget.Page,
            property,
            valueFactory(known[0]),
            new RuleEvidence(
                RuleEvidenceKind.TemplateSection,
                ProviderId,
                reference),
            confidence: hasUnknownSection ? 0.75m : 0.99m,
            needsConfirmation: hasUnknownSection,
            severity: RuleSeverity.Error,
            repairLevel: property is
                FormatProperty.PageWidth
                or FormatProperty.PageHeight
                or FormatProperty.PageOrientation
                or FormatProperty.MarginTop
                or FormatProperty.MarginRight
                or FormatProperty.MarginBottom
                or FormatProperty.MarginLeft
                ? RepairLevel.RequiresConfirmation
                : RepairLevel.None);
    }

    private void ExtractTextRules(
        DocumentModel document,
        ICollection<FormatRule> rules,
        List<RulePackageNotice> notices)
    {
        ParagraphModel[] paragraphs = document.Sections
            .SelectMany(section => section.Paragraphs)
            .Where(paragraph => paragraph.Text.Length > 0)
            .OrderBy(paragraph => paragraph.Location)
            .ToArray();

        var selected = new Dictionary<RuleTarget, TargetCandidate>();
        AddPositionalCandidates(paragraphs, selected);
        AddPatternCandidate(
            paragraphs,
            selected,
            RuleTarget.Abstract,
            text => StartsWith(text, "Abstract")
                || StartsWith(text, "摘要"),
            confidence: 0.99m);
        AddPatternCandidate(
            paragraphs,
            selected,
            RuleTarget.Keywords,
            text => StartsWith(text, "Index Terms")
                || StartsWith(text, "Keywords")
                || StartsWith(text, "关键词"),
            confidence: 0.99m);
        AddPatternCandidate(
            paragraphs,
            selected,
            RuleTarget.Heading1,
            LooksLikeHeading1,
            confidence: 0.97m);
        AddPatternCandidate(
            paragraphs,
            selected,
            RuleTarget.Heading2,
            LooksLikeHeading2,
            confidence: 0.96m);
        AddPatternCandidate(
            paragraphs,
            selected,
            RuleTarget.Heading3,
            LooksLikeHeading3,
            confidence: 0.94m);
        AddHeadingStyleCandidates(document, paragraphs, selected);
        AddPatternCandidate(
            paragraphs,
            selected,
            RuleTarget.FigureCaption,
            text => StartsWith(text, "Fig.")
                || StartsWith(text, "Figure")
                || StartsWith(text, "图"),
            confidence: 0.99m);
        AddPatternCandidate(
            paragraphs,
            selected,
            RuleTarget.TableCaption,
            text => StartsWith(text, "Table")
                || StartsWith(text, "TABLE")
                || StartsWith(text, "表"),
            confidence: 0.99m);
        AddPatternCandidate(
            paragraphs,
            selected,
            RuleTarget.ReferencesHeading,
            IsReferencesHeading,
            confidence: 0.99m);
        AddReferenceEntryCandidate(paragraphs, selected);
        AddBodyCandidate(paragraphs, selected);
        AddTableTextCandidate(document, selected);

        foreach (RuleTarget target in TextTargets)
        {
            if (!selected.TryGetValue(target, out TargetCandidate? candidate))
            {
                notices.Add(new RulePackageNotice(
                    $"template.{TargetId(target)}.missing",
                    RuleNoticeSeverity.Information,
                    "No unambiguous template sample was found for this element.",
                    $"target:{TargetId(target)}"));
                continue;
            }

            AddTargetRules(document, rules, target, candidate);
        }
    }

    private static void AddPositionalCandidates(
        IReadOnlyList<ParagraphModel> paragraphs,
        IDictionary<RuleTarget, TargetCandidate> selected)
    {
        if (paragraphs.Count > 0)
        {
            selected[RuleTarget.Title] = new(
                paragraphs[0],
                Confidence: 0.88m,
                NeedsConfirmation: false);
        }

        if (paragraphs.Count > 1)
        {
            selected[RuleTarget.Author] = new(
                paragraphs[1],
                Confidence: 0.85m,
                NeedsConfirmation: false);
        }

        if (paragraphs.Count > 2)
        {
            selected[RuleTarget.Affiliation] = new(
                paragraphs[2],
                Confidence: 0.80m,
                NeedsConfirmation: false);
        }
    }

    private static void AddPatternCandidate(
        IEnumerable<ParagraphModel> paragraphs,
        IDictionary<RuleTarget, TargetCandidate> selected,
        RuleTarget target,
        Func<string, bool> predicate,
        decimal confidence)
    {
        ParagraphModel[] matches = paragraphs
            .Where(paragraph => predicate(paragraph.Text.Value.Trim()))
            .ToArray();
        if (matches.Length == 0)
        {
            return;
        }

        selected[target] = new TargetCandidate(
            MostCommonStyleSample(matches),
            confidence,
            NeedsConfirmation: HasStyleTie(matches));
    }

    private static void AddHeadingStyleCandidates(
        DocumentModel document,
        IReadOnlyList<ParagraphModel> paragraphs,
        IDictionary<RuleTarget, TargetCandidate> selected)
    {
        IReadOnlyDictionary<string, StyleDefinition> styles =
            document.Styles.ToDictionary(
                style => style.StyleId,
                StringComparer.Ordinal);
        AddStyleCandidate(
            paragraphs,
            styles,
            selected,
            RuleTarget.Heading1,
            ["heading1", "sectionheading", "sectiontitle", "level1heading", "head1"]);
        AddStyleCandidate(
            paragraphs,
            styles,
            selected,
            RuleTarget.Heading2,
            ["heading2", "subsectionheading", "subsectiontitle", "level2heading", "head2"]);
        AddStyleCandidate(
            paragraphs,
            styles,
            selected,
            RuleTarget.Heading3,
            [
                "heading3",
                "subsubsectionheading",
                "subsubsectiontitle",
                "level3heading",
                "head3",
            ]);
    }

    private static void AddStyleCandidate(
        IEnumerable<ParagraphModel> paragraphs,
        IReadOnlyDictionary<string, StyleDefinition> styles,
        IDictionary<RuleTarget, TargetCandidate> selected,
        RuleTarget target,
        IReadOnlyCollection<string> markers)
    {
        ParagraphModel[] matches = paragraphs
            .Where(paragraph => paragraph.StyleId is not null)
            .Where(
                paragraph => styles.TryGetValue(
                    paragraph.StyleId!,
                    out StyleDefinition? style)
                    && StyleMatches(style, markers))
            .ToArray();
        if (matches.Length == 0)
        {
            return;
        }

        selected[target] = new TargetCandidate(
            MostCommonStyleSample(matches),
            Confidence: 0.97m,
            NeedsConfirmation: HasStyleTie(matches));
    }

    private static bool StyleMatches(
        StyleDefinition style,
        IReadOnlyCollection<string> markers) =>
        markers.Contains(NormalizeStyleToken(style.StyleId))
        || (style.Name is not null
            && markers.Contains(NormalizeStyleToken(style.Name)));

    private static string NormalizeStyleToken(string value) =>
        new(
            value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

    private static void AddReferenceEntryCandidate(
        IReadOnlyList<ParagraphModel> paragraphs,
        IDictionary<RuleTarget, TargetCandidate> selected)
    {
        ParagraphModel[] matches = paragraphs
            .Where(
                paragraph =>
                {
                    string text = paragraph.Text.Value.TrimStart();
                    return text.Length >= 3
                        && text[0] == '['
                        && char.IsDigit(text[1])
                        && text.Contains(']');
                })
            .ToArray();
        if (matches.Length > 0)
        {
            selected[RuleTarget.ReferenceEntry] = new TargetCandidate(
                MostCommonStyleSample(matches),
                Confidence: 0.96m,
                NeedsConfirmation: HasStyleTie(matches));
        }
    }

    private static void AddBodyCandidate(
        IEnumerable<ParagraphModel> paragraphs,
        IDictionary<RuleTarget, TargetCandidate> selected)
    {
        HashSet<ParagraphModel> alreadySelected = selected.Values
            .Select(candidate => candidate.Paragraph)
            .ToHashSet();
        ParagraphModel[] candidates = paragraphs
            .Where(paragraph => !alreadySelected.Contains(paragraph))
            .Where(paragraph => paragraph.Text.Length >= 40)
            .ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        ParagraphModel sample = MostCommonStyleSample(candidates);
        int matchingStyleCount = candidates.Count(
            paragraph => string.Equals(
                paragraph.StyleId,
                sample.StyleId,
                StringComparison.Ordinal));
        decimal confidence = matchingStyleCount >= 2 ? 0.92m : 0.72m;
        selected[RuleTarget.Body] = new TargetCandidate(
            sample,
            confidence,
            NeedsConfirmation: HasStyleTie(candidates)
                || matchingStyleCount < 2);
    }

    private static void AddTableTextCandidate(
        DocumentModel document,
        IDictionary<RuleTarget, TargetCandidate> selected)
    {
        ParagraphModel[] paragraphs = document.Sections
            .SelectMany(section => section.Tables)
            .SelectMany(table => table.Rows)
            .SelectMany(row => row.Cells)
            .SelectMany(cell => cell.Paragraphs)
            .Where(paragraph => paragraph.Text.Length > 0)
            .ToArray();
        if (paragraphs.Length > 0)
        {
            selected[RuleTarget.TableText] = new TargetCandidate(
                MostCommonStyleSample(paragraphs),
                Confidence: 0.96m,
                NeedsConfirmation: HasStyleTie(paragraphs));
        }
    }

    private void AddTargetRules(
        DocumentModel document,
        ICollection<FormatRule> rules,
        RuleTarget target,
        TargetCandidate candidate)
    {
        ParagraphModel paragraph = candidate.Paragraph;
        string reference = paragraph.Location.ToString();
        RuleEvidenceKind evidenceKind = RuleEvidenceKind.TemplateSample;
        if (paragraph.StyleId is not null)
        {
            reference += $";style:{paragraph.StyleId}";
        }

        // Word style identifiers are package-local implementation details.
        // Compare resolved formatting across documents instead of requiring a
        // manuscript to reuse the template package's raw style ID.

        AddParagraphFormattingRules(
            rules,
            target,
            paragraph.EffectiveFormatting,
            new RuleEvidence(evidenceKind, ProviderId, reference),
            candidate);

        CharacterFormatting character = CharacterFormattingFor(
            document,
            paragraph);
        AddCharacterFormattingRules(
            rules,
            target,
            character,
            new RuleEvidence(evidenceKind, ProviderId, reference),
            candidate);
    }

    private static void AddParagraphFormattingRules(
        ICollection<FormatRule> rules,
        RuleTarget target,
        ParagraphFormatting formatting,
        RuleEvidence evidence,
        TargetCandidate candidate)
    {
        if (target == RuleTarget.TableText)
        {
            return;
        }

        if (formatting.Alignment is { } alignment)
        {
            AddRule(
                rules,
                target,
                FormatProperty.ParagraphAlignment,
                new ParagraphAlignmentRuleValue(alignment),
                evidence,
                candidate.Confidence,
                candidate.NeedsConfirmation);
        }

        if (formatting.LineSpacing is not null)
        {
            AddRule(
                rules,
                target,
                FormatProperty.LineSpacing,
                new LineSpacingRuleValue(formatting.LineSpacing),
                evidence,
                candidate.Confidence,
                candidate.NeedsConfirmation);
        }

        AddOptionalTwip(
            rules,
            target,
            FormatProperty.SpaceBefore,
            formatting.SpaceBefore,
            evidence,
            candidate);
        AddOptionalTwip(
            rules,
            target,
            FormatProperty.SpaceAfter,
            formatting.SpaceAfter,
            evidence,
            candidate);
        AddOptionalTwip(
            rules,
            target,
            FormatProperty.FirstLineIndent,
            formatting.Indentation?.FirstLine,
            evidence,
            candidate);
    }

    private static void AddCharacterFormattingRules(
        ICollection<FormatRule> rules,
        RuleTarget target,
        CharacterFormatting formatting,
        RuleEvidence evidence,
        TargetCandidate candidate)
    {
        AddOptionalText(
            rules,
            target,
            FormatProperty.FontAscii,
            formatting.Fonts?.Ascii,
            evidence,
            candidate);
        AddOptionalText(
            rules,
            target,
            FormatProperty.FontHighAnsi,
            formatting.Fonts?.HighAnsi,
            evidence,
            candidate);
        AddOptionalText(
            rules,
            target,
            FormatProperty.FontEastAsia,
            formatting.Fonts?.EastAsia,
            evidence,
            candidate);
        AddOptionalText(
            rules,
            target,
            FormatProperty.FontComplexScript,
            formatting.Fonts?.ComplexScript,
            evidence,
            candidate);
        AddOptionalTwip(
            rules,
            target,
            FormatProperty.FontSize,
            formatting.FontSize,
            evidence,
            candidate);

        if (target != RuleTarget.TableText
            && formatting.Bold is { } bold)
        {
            AddRule(
                rules,
                target,
                FormatProperty.Bold,
                new BooleanRuleValue(bold),
                evidence,
                candidate.Confidence,
                candidate.NeedsConfirmation);
        }

        if (target != RuleTarget.TableText
            && formatting.Italic is { } italic)
        {
            AddRule(
                rules,
                target,
                FormatProperty.Italic,
                new BooleanRuleValue(italic),
                evidence,
                candidate.Confidence,
                candidate.NeedsConfirmation);
        }
    }

    private static void AddOptionalTwip(
        ICollection<FormatRule> rules,
        RuleTarget target,
        FormatProperty property,
        Twip? value,
        RuleEvidence evidence,
        TargetCandidate candidate)
    {
        if (value is not null)
        {
            AddRule(
                rules,
                target,
                property,
                new TwipRuleValue(value.Value),
                evidence,
                candidate.Confidence,
                candidate.NeedsConfirmation);
        }
    }

    private static void AddOptionalText(
        ICollection<FormatRule> rules,
        RuleTarget target,
        FormatProperty property,
        string? value,
        RuleEvidence evidence,
        TargetCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            AddRule(
                rules,
                target,
                property,
                new TextRuleValue(value),
                evidence,
                candidate.Confidence,
                candidate.NeedsConfirmation);
        }
    }

    private static void AddRule(
        ICollection<FormatRule> rules,
        RuleTarget target,
        FormatProperty property,
        RuleValue value,
        RuleEvidence evidence,
        decimal confidence,
        bool needsConfirmation,
        RuleSeverity severity = RuleSeverity.Warning,
        RepairLevel repairLevel = RepairLevel.Safe)
    {
        rules.Add(new FormatRule(
            $"template-v1.{TargetId(target)}.{PropertyId(property)}",
            target,
            property,
            value,
            severity,
            repairLevel,
            evidence,
            confidence,
            needsConfirmation: needsConfirmation));
    }

    private static CharacterFormatting CharacterFormattingFor(
        DocumentModel document,
        ParagraphModel paragraph)
    {
        if (paragraph.StyleId is not null)
        {
            StyleDefinition? style = document.Styles.FirstOrDefault(
                candidate => string.Equals(
                    candidate.StyleId,
                    paragraph.StyleId,
                    StringComparison.Ordinal));
            if (style is not null)
            {
                return style.CharacterFormatting;
            }
        }

        RunModel[] runs = paragraph.Runs
            .Where(run => run.Text.Length > 0)
            .ToArray();
        if (runs.Length == 0)
        {
            return CharacterFormatting.Empty;
        }

        return new CharacterFormatting(
            Fonts: Uniform(runs.Select(run => run.EffectiveFormatting.Fonts)),
            FontSize: Uniform(
                runs.Select(run => run.EffectiveFormatting.FontSize)),
            Bold: Uniform(runs.Select(run => run.EffectiveFormatting.Bold)),
            Italic: Uniform(runs.Select(run => run.EffectiveFormatting.Italic)),
            AllCaps: Uniform(
                runs.Select(run => run.EffectiveFormatting.AllCaps)),
            SmallCaps: Uniform(
                runs.Select(run => run.EffectiveFormatting.SmallCaps)));
    }

    private static T? Uniform<T>(IEnumerable<T?> values)
    {
        T?[] known = values
            .Where(value => value is not null)
            .Distinct()
            .ToArray();
        return known.Length == 1 ? known[0] : default;
    }

    private static ParagraphModel MostCommonStyleSample(
        IEnumerable<ParagraphModel> paragraphs) =>
        paragraphs
            .GroupBy(paragraph => paragraph.StyleId, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .First()
            .OrderBy(paragraph => paragraph.Location)
            .First();

    private static bool HasStyleTie(IEnumerable<ParagraphModel> paragraphs)
    {
        int[] counts = paragraphs
            .GroupBy(paragraph => paragraph.StyleId, StringComparer.Ordinal)
            .Select(group => group.Count())
            .OrderByDescending(count => count)
            .Take(2)
            .ToArray();
        return counts.Length > 1 && counts[0] == counts[1];
    }

    private static bool StartsWith(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsReferencesHeading(string text) =>
        string.Equals(
            text.Trim(),
            "References",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(text.Trim(), "参考文献", StringComparison.Ordinal);

    private static bool LooksLikeHeading1(string text)
    {
        string trimmed = text.Trim();
        int separator = trimmed.IndexOf('.');
        if (separator is <= 0 or > 7 || separator + 1 >= trimmed.Length)
        {
            return false;
        }

        string prefix = trimmed[..separator];
        return prefix.All(character =>
                "IVXLCDM".Contains(
                    char.ToUpperInvariant(character),
                    StringComparison.Ordinal))
            && trimmed[(separator + 1)..].Any(char.IsLetter);
    }

    private static bool LooksLikeHeading2(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length >= 4
            && char.IsLetter(trimmed[0])
            && trimmed[1] == '.'
            && char.IsWhiteSpace(trimmed[2]);
    }

    private static bool LooksLikeHeading3(string text)
    {
        string trimmed = text.Trim();
        int close = trimmed.IndexOf(')');
        return close is > 0 and <= 3
            && trimmed[..close].All(char.IsDigit)
            && close + 1 < trimmed.Length;
    }

    private static string PackageId(
        WordTemplateFormatRequirementSource template)
    {
        string signature = string.Join(
            "|",
            template.SourceName,
            template.Document.PackageKind,
            template.Document.Sections.Count,
            string.Join(
                ",",
                template.Document.Styles.Select(style => style.StyleId)));
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
        return $"template-v1-{digest[..16].ToLowerInvariant()}";
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

    private static readonly RuleTarget[] TextTargets =
    [
        RuleTarget.Title,
        RuleTarget.Author,
        RuleTarget.Affiliation,
        RuleTarget.Abstract,
        RuleTarget.Keywords,
        RuleTarget.Heading1,
        RuleTarget.Heading2,
        RuleTarget.Heading3,
        RuleTarget.Body,
        RuleTarget.FigureCaption,
        RuleTarget.TableCaption,
        RuleTarget.TableText,
        RuleTarget.ReferencesHeading,
        RuleTarget.ReferenceEntry,
    ];

    private sealed record TargetCandidate(
        ParagraphModel Paragraph,
        decimal Confidence,
        bool NeedsConfirmation);
}
