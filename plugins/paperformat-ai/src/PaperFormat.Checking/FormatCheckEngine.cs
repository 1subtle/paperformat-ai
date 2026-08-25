using System.Security.Cryptography;
using System.Text;
using PaperFormat.Domain;

namespace PaperFormat.Checking;

/// <summary>
/// Deterministic comparison engine for the MVP-supported formatting rules.
/// </summary>
public sealed class FormatCheckEngine : IFormatChecker
{
    public CheckReport Check(
        DocumentModel document,
        RulePackage rules,
        ClassificationSet classifications)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(classifications);

        Dictionary<string, ParagraphModel> paragraphs =
            IndexParagraphs(document);
        List<CheckIssue> issues = [];
        List<SkippedRule> skipped = [];
        PendingElement[] pending = classifications.Elements
            .Where(
                element => element.Status
                    == ClassificationStatus.NeedsConfirmation)
            .Select(
                element => new PendingElement(
                    element.ElementId,
                    element.Location,
                    element.Kind,
                    element.Confidence))
            .OrderBy(element => element.Location)
            .ToArray();
        var observations = new ObservationCounter();
        HashSet<int> bodySectionIndexes = classifications.Elements
            .Where(IsConfirmed)
            .Where(
                element => element.Kind is
                    ManuscriptElementKind.Heading1
                    or ManuscriptElementKind.Heading2
                    or ManuscriptElementKind.Heading3
                    or ManuscriptElementKind.Body
                    or ManuscriptElementKind.ReferencesHeading
                    or ManuscriptElementKind.ReferenceEntry)
            .Where(element => element.Location.SectionIndex is not null)
            .Select(element => element.Location.SectionIndex!.Value)
            .ToHashSet();
        FormatRule[] enabledRules = rules.Rules
            .Where(rule => rule.Enabled)
            .ToArray();

        foreach (FormatRule rule in enabledRules)
        {
            if (rule.NeedsConfirmation)
            {
                skipped.Add(new SkippedRule(
                    rule.RuleId,
                    "rule_needs_confirmation",
                    "The rule must be confirmed before it can be evaluated."));
                continue;
            }

            if (rule.Target == RuleTarget.Page)
            {
                EvaluatePageRule(
                    document,
                    rule,
                    issues,
                    skipped,
                    observations,
                    bodySectionIndexes);
                continue;
            }

            if (!TryMapTarget(rule.Target, out ManuscriptElementKind kind))
            {
                skipped.Add(Unsupported(rule));
                continue;
            }

            DocumentElement[] matching = classifications.Elements
                .Where(element => element.Kind == kind)
                .Where(IsConfirmed)
                .ToArray();
            bool hasPending = classifications.Elements.Any(
                element => element.Kind == kind
                    && element.Status
                        == ClassificationStatus.NeedsConfirmation);
            if (hasPending)
            {
                skipped.Add(new SkippedRule(
                    rule.RuleId,
                    "classification_pending",
                    "At least one matching element requires user confirmation."));
            }

            if (matching.Length == 0)
            {
                if (!hasPending)
                {
                    skipped.Add(new SkippedRule(
                        rule.RuleId,
                        "no_matching_element",
                        "The document has no confirmed matching element."));
                }

                continue;
            }

            if (rule.Property == FormatProperty.CaptionNumberSequence)
            {
                EvaluateCaptionSequence(
                    rule,
                    matching,
                    paragraphs,
                    issues,
                    observations);
                continue;
            }

            foreach (DocumentElement element in matching)
            {
                if (!paragraphs.TryGetValue(
                        element.Location.ToString(),
                        out ParagraphModel? paragraph))
                {
                    skipped.Add(new SkippedRule(
                        rule.RuleId,
                        "location_not_found",
                        "A classified element location was not found in the parsed document."));
                    continue;
                }

                EvaluateElementRule(
                    rule,
                    element,
                    paragraph,
                    issues,
                    skipped,
                    observations);
            }
        }

        CheckIssue[] orderedIssues = issues
            .OrderBy(issue => issue.Severity)
            .ThenBy(issue => issue.RuleId, StringComparer.Ordinal)
            .ThenBy(issue => issue.DocumentLocation)
            .ThenBy(issue => issue.IssueId, StringComparer.Ordinal)
            .ToArray();
        SkippedRule[] orderedSkipped = skipped
            .Distinct()
            .OrderBy(item => item.RuleId, StringComparer.Ordinal)
            .ThenBy(item => item.ReasonCode, StringComparer.Ordinal)
            .ToArray();
        int score = observations.Total == 0
            ? 100
            : (int)decimal.Round(
                observations.Passed * 100m / observations.Total,
                0,
                MidpointRounding.AwayFromZero);
        var summary = new CheckSummary(
            enabledRules.Length,
            observations.Total,
            observations.Passed,
            orderedIssues.Length,
            orderedIssues.Count(
                issue => issue.Severity == RuleSeverity.Error),
            orderedIssues.Count(
                issue => issue.Severity == RuleSeverity.Warning),
            orderedIssues.Count(
                issue => issue.Severity == RuleSeverity.Information),
            orderedSkipped.Length,
            pending.Length,
            score);
        bool needsConfirmation = orderedSkipped.Any(
            item => item.ReasonCode == "rule_needs_confirmation");
        CheckStatus status = orderedIssues.Length > 0
            ? CheckStatus.IssuesFound
            : needsConfirmation
                ? CheckStatus.NeedsConfirmation
                : CheckStatus.Passed;
        string reportId = ReportId(
            rules,
            orderedIssues,
            orderedSkipped,
            pending);

        return new CheckReport(
            reportId,
            rules.PackageId,
            rules.Revision,
            status,
            summary,
            orderedIssues,
            orderedSkipped,
            pending);
    }

    private static void EvaluatePageRule(
        DocumentModel document,
        FormatRule rule,
        ICollection<CheckIssue> issues,
        List<SkippedRule> skipped,
        ObservationCounter observations,
        HashSet<int> bodySectionIndexes)
    {
        bool supported = false;
        foreach (SectionModel section in document.Sections)
        {
            if (rule.Property is
                    FormatProperty.ColumnCount
                    or FormatProperty.ColumnSpacing
                && bodySectionIndexes.Count > 0
                && section.Location.SectionIndex is { } sectionIndex
                && !bodySectionIndexes.Contains(sectionIndex))
            {
                continue;
            }

            RuleValue? current = PageValue(section.PageSettings, rule.Property);
            if (current is null && !IsSupportedPageProperty(rule.Property))
            {
                continue;
            }

            supported = true;
            Observe(
                rule,
                RuleTarget.Page,
                section.Location,
                current,
                confidence: rule.Confidence,
                issues,
                observations);
        }

        if (!supported)
        {
            skipped.Add(Unsupported(rule));
        }
    }

    private static void EvaluateElementRule(
        FormatRule rule,
        DocumentElement element,
        ParagraphModel paragraph,
        ICollection<CheckIssue> issues,
        List<SkippedRule> skipped,
        ObservationCounter observations)
    {
        if (IsCharacterProperty(rule.Property))
        {
            RunModel[] runs = paragraph.Runs
                .Where(run => run.Text.Length > 0)
                .ToArray();
            if (runs.Length == 0)
            {
                skipped.Add(new SkippedRule(
                    rule.RuleId,
                    "no_text_run",
                    "The matching element has no text run to evaluate."));
                return;
            }

            foreach (RunModel run in runs)
            {
                Observe(
                    rule,
                    rule.Target,
                    run.Location,
                    CharacterValue(run.EffectiveFormatting, rule.Property),
                    rule.Confidence * element.Confidence,
                    issues,
                    observations);
            }

            return;
        }

        RuleValue? current = rule.Property switch
        {
            FormatProperty.ParagraphStyleId =>
                paragraph.StyleId is null
                    ? null
                    : new TextRuleValue(paragraph.StyleId),
            FormatProperty.ParagraphAlignment =>
                new ParagraphAlignmentRuleValue(
                    paragraph.EffectiveFormatting.Alignment
                    ?? ParagraphAlignment.Left),
            FormatProperty.LineSpacing =>
                paragraph.EffectiveFormatting.LineSpacing is null
                    ? null
                    : new LineSpacingRuleValue(
                        paragraph.EffectiveFormatting.LineSpacing),
            FormatProperty.SpaceBefore =>
                new TwipRuleValue(
                    paragraph.EffectiveFormatting.SpaceBefore ?? new Twip(0)),
            FormatProperty.SpaceAfter =>
                new TwipRuleValue(
                    paragraph.EffectiveFormatting.SpaceAfter ?? new Twip(0)),
            FormatProperty.FirstLineIndent =>
                new TwipRuleValue(
                    paragraph.EffectiveFormatting.Indentation?.FirstLine
                    ?? new Twip(0)),
            FormatProperty.DirectFormattingConsistency =>
                new BooleanRuleValue(!HasSupportedDirectFormatting(paragraph)),
            _ => null,
        };
        if (current is null
            && !IsSupportedParagraphProperty(rule.Property))
        {
            skipped.Add(Unsupported(rule));
            return;
        }

        Observe(
            rule,
            rule.Target,
            paragraph.Location,
            current,
            rule.Confidence * element.Confidence,
            issues,
            observations);
    }

    private static void EvaluateCaptionSequence(
        FormatRule rule,
        IEnumerable<DocumentElement> elements,
        Dictionary<string, ParagraphModel> paragraphs,
        ICollection<CheckIssue> issues,
        ObservationCounter observations)
    {
        int expectedNumber = 1;
        foreach (DocumentElement element in elements.OrderBy(item => item.Location))
        {
            paragraphs.TryGetValue(
                element.Location.ToString(),
                out ParagraphModel? paragraph);
            int? actualNumber = paragraph is null
                ? null
                : CaptionNumber(paragraph.Text.Value, rule.Target);
            bool isSequential = actualNumber == expectedNumber;
            Observe(
                rule,
                rule.Target,
                element.Location,
                new BooleanRuleValue(isSequential),
                rule.Confidence * element.Confidence,
                issues,
                observations);
            expectedNumber++;
        }
    }

    private static void Observe(
        FormatRule rule,
        RuleTarget target,
        StructuralLocation location,
        RuleValue? current,
        decimal confidence,
        ICollection<CheckIssue> issues,
        ObservationCounter observations,
        bool repairTargetAvailable = true)
    {
        observations.Total++;
        if (RuleValuesEqual(rule.Property, current, rule.Expected))
        {
            observations.Passed++;
            return;
        }

        issues.Add(new CheckIssue(
            IssueId(rule.RuleId, location, current, rule.Expected),
            rule.RuleId,
            rule.Severity,
            target,
            location,
            current,
            rule.Expected,
            $"The {target} {rule.Property} value does not match the confirmed rule.",
            rule.Evidence,
            Math.Clamp(confidence, 0m, 1m),
            rule.RepairLevel != RepairLevel.None
                && repairTargetAvailable));
    }

    private static RuleValue? PageValue(
        PageSettings page,
        FormatProperty property) =>
        property switch
        {
            FormatProperty.PageWidth =>
                page.Width is null ? null : new TwipRuleValue(page.Width.Value),
            FormatProperty.PageHeight =>
                page.Height is null
                    ? null
                    : new TwipRuleValue(page.Height.Value),
            FormatProperty.PageOrientation =>
                new PageOrientationRuleValue(
                    page.Orientation ?? PageOrientation.Portrait),
            FormatProperty.MarginTop =>
                page.Margins.Top is null
                    ? null
                    : new TwipRuleValue(page.Margins.Top.Value),
            FormatProperty.MarginRight =>
                page.Margins.Right is null
                    ? null
                    : new TwipRuleValue(page.Margins.Right.Value),
            FormatProperty.MarginBottom =>
                page.Margins.Bottom is null
                    ? null
                    : new TwipRuleValue(page.Margins.Bottom.Value),
            FormatProperty.MarginLeft =>
                page.Margins.Left is null
                    ? null
                    : new TwipRuleValue(page.Margins.Left.Value),
            FormatProperty.ColumnCount =>
                page.Columns.Count is null
                    ? null
                    : new IntegerRuleValue(page.Columns.Count.Value),
            FormatProperty.ColumnSpacing =>
                page.Columns.Spacing is null
                    ? null
                    : new TwipRuleValue(page.Columns.Spacing.Value),
            _ => null,
        };

    private static RuleValue? CharacterValue(
        CharacterFormatting formatting,
        FormatProperty property) =>
        property switch
        {
            FormatProperty.FontAscii =>
                Text(formatting.Fonts?.Ascii),
            FormatProperty.FontHighAnsi =>
                Text(formatting.Fonts?.HighAnsi),
            FormatProperty.FontEastAsia =>
                Text(formatting.Fonts?.EastAsia),
            FormatProperty.FontComplexScript =>
                Text(formatting.Fonts?.ComplexScript),
            FormatProperty.FontSize =>
                formatting.FontSize is null
                    ? null
                    : new TwipRuleValue(formatting.FontSize.Value),
            FormatProperty.Bold =>
                new BooleanRuleValue(formatting.Bold ?? false),
            FormatProperty.Italic =>
                new BooleanRuleValue(formatting.Italic ?? false),
            _ => null,
        };

    private static TextRuleValue? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new TextRuleValue(value);

    private static bool RuleValuesEqual(
        FormatProperty property,
        RuleValue? current,
        RuleValue expected)
    {
        if (current is TextRuleValue currentText
            && expected is TextRuleValue expectedText)
        {
            StringComparison comparison = property is
                FormatProperty.FontAscii
                or FormatProperty.FontHighAnsi
                or FormatProperty.FontEastAsia
                or FormatProperty.FontComplexScript
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                currentText.Value,
                expectedText.Value,
                comparison);
        }

        return Equals(current, expected);
    }

    private static bool HasSupportedDirectFormatting(
        ParagraphModel paragraph) =>
        !paragraph.DirectFormatting.IsEmpty
        || paragraph.Runs.Any(run => !run.DirectFormatting.IsEmpty);

    private static bool IsSupportedPageProperty(FormatProperty property) =>
        property is
            FormatProperty.PageWidth
            or FormatProperty.PageHeight
            or FormatProperty.PageOrientation
            or FormatProperty.MarginTop
            or FormatProperty.MarginRight
            or FormatProperty.MarginBottom
            or FormatProperty.MarginLeft
            or FormatProperty.ColumnCount
            or FormatProperty.ColumnSpacing;

    private static bool IsCharacterProperty(FormatProperty property) =>
        property is
            FormatProperty.FontAscii
            or FormatProperty.FontHighAnsi
            or FormatProperty.FontEastAsia
            or FormatProperty.FontComplexScript
            or FormatProperty.FontSize
            or FormatProperty.Bold
            or FormatProperty.Italic;

    private static bool IsSupportedParagraphProperty(FormatProperty property) =>
        property is
            FormatProperty.ParagraphStyleId
            or FormatProperty.ParagraphAlignment
            or FormatProperty.LineSpacing
            or FormatProperty.SpaceBefore
            or FormatProperty.SpaceAfter
            or FormatProperty.FirstLineIndent
            or FormatProperty.DirectFormattingConsistency;

    private static bool TryMapTarget(
        RuleTarget target,
        out ManuscriptElementKind kind)
    {
        kind = target switch
        {
            RuleTarget.Title => ManuscriptElementKind.Title,
            RuleTarget.Author => ManuscriptElementKind.Author,
            RuleTarget.Affiliation => ManuscriptElementKind.Affiliation,
            RuleTarget.Abstract => ManuscriptElementKind.Abstract,
            RuleTarget.Keywords => ManuscriptElementKind.Keywords,
            RuleTarget.Heading1 => ManuscriptElementKind.Heading1,
            RuleTarget.Heading2 => ManuscriptElementKind.Heading2,
            RuleTarget.Heading3 => ManuscriptElementKind.Heading3,
            RuleTarget.Body => ManuscriptElementKind.Body,
            RuleTarget.FigureCaption => ManuscriptElementKind.FigureCaption,
            RuleTarget.TableCaption => ManuscriptElementKind.TableCaption,
            RuleTarget.TableText => ManuscriptElementKind.TableText,
            RuleTarget.ReferencesHeading =>
                ManuscriptElementKind.ReferencesHeading,
            RuleTarget.ReferenceEntry => ManuscriptElementKind.ReferenceEntry,
            _ => ManuscriptElementKind.Unclassified,
        };
        return kind != ManuscriptElementKind.Unclassified;
    }

    private static bool IsConfirmed(DocumentElement element) =>
        element.Status is
            ClassificationStatus.Confirmed
            or ClassificationStatus.UserConfirmed;

    private static Dictionary<string, ParagraphModel> IndexParagraphs(
        DocumentModel document)
    {
        IEnumerable<ParagraphModel> paragraphs = document.Sections
            .SelectMany(
                section => section.Paragraphs.Concat(
                    section.Tables
                        .SelectMany(table => table.Rows)
                        .SelectMany(row => row.Cells)
                        .SelectMany(cell => cell.Paragraphs)));
        return paragraphs.ToDictionary(
            paragraph => paragraph.Location.ToString(),
            StringComparer.Ordinal);
    }

    private static int? CaptionNumber(string text, RuleTarget target)
    {
        string trimmed = text.TrimStart();
        if (target == RuleTarget.FigureCaption)
        {
            int firstDigit = IndexOfDigit(trimmed);
            return firstDigit < 0
                ? null
                : ParseDecimalPrefix(trimmed.AsSpan(firstDigit));
        }

        if (target == RuleTarget.TableCaption)
        {
            int separator = trimmed.IndexOf(' ');
            if (separator < 0 || separator + 1 >= trimmed.Length)
            {
                return null;
            }

            ReadOnlySpan<char> token = trimmed.AsSpan(separator + 1)
                .TrimStart();
            int length = 0;
            while (length < token.Length && char.IsLetterOrDigit(token[length]))
            {
                length++;
            }

            token = token[..length];
            int? decimalValue = ParseDecimalPrefix(token);
            return decimalValue ?? ParseRoman(token);
        }

        return null;
    }

    private static int IndexOfDigit(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (char.IsDigit(value[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static int? ParseDecimalPrefix(ReadOnlySpan<char> value)
    {
        int length = 0;
        while (length < value.Length && char.IsDigit(value[length]))
        {
            length++;
        }

        return length > 0 && int.TryParse(value[..length], out int result)
            ? result
            : null;
    }

    private static int? ParseRoman(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return null;
        }

        int total = 0;
        int previous = 0;
        for (int index = value.Length - 1; index >= 0; index--)
        {
            int current = char.ToUpperInvariant(value[index]) switch
            {
                'I' => 1,
                'V' => 5,
                'X' => 10,
                'L' => 50,
                'C' => 100,
                'D' => 500,
                'M' => 1000,
                _ => 0,
            };
            if (current == 0)
            {
                return null;
            }

            total += current < previous ? -current : current;
            previous = current;
        }

        return total > 0 ? total : null;
    }

    private static SkippedRule Unsupported(FormatRule rule) =>
        new(
            rule.RuleId,
            "unsupported_rule",
            "The rule target and property combination is not supported.");

    private static string IssueId(
        string ruleId,
        StructuralLocation location,
        RuleValue? current,
        RuleValue expected)
    {
        string input =
            $"{ruleId}|{location}|{ValueKey(current)}|{ValueKey(expected)}";
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        return $"issue-{digest[..20].ToLowerInvariant()}";
    }

    private static string ReportId(
        RulePackage rules,
        IEnumerable<CheckIssue> issues,
        IEnumerable<SkippedRule> skipped,
        IEnumerable<PendingElement> pending)
    {
        string input = string.Join(
            "|",
            rules.PackageId,
            rules.Revision,
            string.Join(",", issues.Select(issue => issue.IssueId)),
            string.Join(
                ",",
                skipped.Select(
                    item => $"{item.RuleId}:{item.ReasonCode}")),
            string.Join(",", pending.Select(item => item.ElementId)));
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        return $"check-v1-{digest[..20].ToLowerInvariant()}";
    }

    private static string ValueKey(RuleValue? value) =>
        value switch
        {
            null => "unknown",
            TwipRuleValue item => $"twip:{item.Value.Value}",
            IntegerRuleValue item => $"int:{item.Value}",
            BooleanRuleValue item => $"bool:{item.Value}",
            TextRuleValue item => $"text:{item.Value}",
            PageOrientationRuleValue item => $"orientation:{item.Value}",
            ParagraphAlignmentRuleValue item => $"alignment:{item.Value}",
            LineSpacingRuleValue item =>
                $"spacing:{item.Value.Kind}:" +
                $"{item.Value.Length?.Value}:" +
                $"{item.Value.Multiple?.Value}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported rule value."),
        };

    private sealed class ObservationCounter
    {
        public int Total { get; set; }

        public int Passed { get; set; }
    }
}
