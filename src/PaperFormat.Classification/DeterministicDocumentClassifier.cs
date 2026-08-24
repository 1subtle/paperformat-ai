using System.Text;
using PaperFormat.Domain;

namespace PaperFormat.Classification;

/// <summary>
/// Deterministic classifier combining style, pattern, formatting, position,
/// and neighboring context evidence.
/// </summary>
public sealed class DeterministicDocumentClassifier : IDocumentClassifier
{
    private const decimal ConfirmedThreshold = 0.80m;
    private const decimal FormattingConfirmedThreshold = 0.50m;
    private const decimal PendingThreshold = 0.50m;
    private const decimal RequiredMargin = 0.15m;
    private const decimal FormattingRequiredMargin = 0.10m;

    public ClassificationSet Classify(DocumentModel document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var styles = document.Styles.ToDictionary(
            style => style.StyleId,
            StringComparer.Ordinal);
        ParagraphModel[] paragraphs = document.Sections
            .SelectMany(section => section.Paragraphs)
            .Where(paragraph => paragraph.Text.Length > 0)
            .OrderBy(paragraph => paragraph.Location)
            .ToArray();
        int referencesIndex = Array.FindIndex(
            paragraphs,
            paragraph => IsReferencesHeading(paragraph.Text.Value));
        List<DocumentElement> elements = new(paragraphs.Length);

        for (int index = 0; index < paragraphs.Length; index++)
        {
            ParagraphModel? previous = index == 0 ? null : paragraphs[index - 1];
            elements.Add(ClassifyParagraph(
                paragraphs[index],
                index,
                referencesIndex,
                previous,
                styles));
        }

        foreach (ParagraphModel tableParagraph in document.Sections
                     .SelectMany(section => section.Tables)
                     .SelectMany(table => table.Rows)
                     .SelectMany(row => row.Cells)
                     .SelectMany(cell => cell.Paragraphs)
                     .Where(paragraph => paragraph.Text.Length > 0))
        {
            elements.Add(new DocumentElement(
                ElementId(tableParagraph.Location),
                tableParagraph.Location,
                ManuscriptElementKind.TableText,
                confidence: 1m,
                ClassificationStatus.Confirmed,
                [
                    new ClassificationReason(
                        "classification.table.cell",
                        ClassificationEvidenceKind.TableStructure,
                        weight: 1m,
                        "The paragraph is contained in a Word table cell."),
                ],
                tableParagraph.Text.Length,
                tableParagraph.StyleId));
        }

        return new ClassificationSet(revision: 1, elements);
    }

    private static DocumentElement ClassifyParagraph(
        ParagraphModel paragraph,
        int position,
        int referencesIndex,
        ParagraphModel? previous,
        IReadOnlyDictionary<string, StyleDefinition> styles)
    {
        var candidates =
            new Dictionary<ManuscriptElementKind, CandidateEvidence>();
        string text = paragraph.Text.Value.Trim();
        bool suppressHeadingPatterns = referencesIndex >= 0
            && position > referencesIndex
            && HasStyleMarker(paragraph, styles, "reference");

        AddStyleEvidence(paragraph, styles, candidates);
        AddPatternEvidence(text, candidates, suppressHeadingPatterns);
        AddFormattingEvidence(paragraph, text, candidates);
        AddPositionEvidence(position, candidates);
        AddContextEvidence(
            paragraph,
            position,
            referencesIndex,
            previous,
            candidates);

        CandidateEvidence[] ranked = candidates.Values
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Kind)
            .ToArray();
        if (ranked.Length == 0 || ranked[0].Score < PendingThreshold)
        {
            return new DocumentElement(
                ElementId(paragraph.Location),
                paragraph.Location,
                ManuscriptElementKind.Unclassified,
                ranked.Length == 0 ? 0m : Math.Min(1m, ranked[0].Score),
                ClassificationStatus.Unclassified,
                ranked.Length == 0
                    ? Array.Empty<ClassificationReason>()
                    : ranked[0].Reasons,
                paragraph.Text.Length,
                paragraph.StyleId);
        }

        CandidateEvidence best = ranked[0];
        decimal runnerUp = ranked.Length > 1 ? ranked[1].Score : 0m;
        decimal confidence = Math.Min(1m, best.Score);
        bool formattingEvidenceConfirmed =
            confidence >= FormattingConfirmedThreshold
            && best.Score - runnerUp >= FormattingRequiredMargin
            && HasSemanticFormattingEvidence(best);
        ClassificationStatus status =
            (confidence >= ConfirmedThreshold
                && best.Score - runnerUp >= RequiredMargin)
            || formattingEvidenceConfirmed
                ? ClassificationStatus.Confirmed
                : ClassificationStatus.NeedsConfirmation;

        return new DocumentElement(
            ElementId(paragraph.Location),
            paragraph.Location,
            best.Kind,
            confidence,
            status,
            best.Reasons,
            paragraph.Text.Length,
            paragraph.StyleId);
    }

    private static bool HasSemanticFormattingEvidence(
        CandidateEvidence candidate) =>
        candidate.Reasons.Any(
            reason => reason.EvidenceKind is
                ClassificationEvidenceKind.Style
                or ClassificationEvidenceKind.TextPattern);

    private static void AddStyleEvidence(
        ParagraphModel paragraph,
        IReadOnlyDictionary<string, StyleDefinition> styles,
        IDictionary<ManuscriptElementKind, CandidateEvidence> candidates)
    {
        if (paragraph.StyleId is null)
        {
            return;
        }

        styles.TryGetValue(paragraph.StyleId, out StyleDefinition? style);
        string token = NormalizeStyleToken(
            paragraph.StyleId + " " + (style?.Name ?? string.Empty));
        AddStyleMatch(
            token,
            "title",
            ManuscriptElementKind.Title,
            0.45m,
            candidates);
        AddStyleMatch(
            token,
            "author",
            ManuscriptElementKind.Author,
            0.45m,
            candidates);
        AddStyleMatch(
            token,
            "affiliation",
            ManuscriptElementKind.Affiliation,
            0.45m,
            candidates);
        AddStyleMatch(
            token,
            "abstract",
            ManuscriptElementKind.Abstract,
            0.50m,
            candidates);
        AddStyleMatch(
            token,
            "keyword",
            ManuscriptElementKind.Keywords,
            0.50m,
            candidates);
        AddStyleMatch(
            token,
            "heading1",
            ManuscriptElementKind.Heading1,
            0.45m,
            candidates);
        AddStyleMatch(
            token,
            "heading2",
            ManuscriptElementKind.Heading2,
            0.45m,
            candidates);
        AddStyleMatch(
            token,
            "heading3",
            ManuscriptElementKind.Heading3,
            0.45m,
            candidates);
        AddStyleMatch(
            token,
            "body",
            ManuscriptElementKind.Body,
            0.35m,
            candidates);
        AddStyleMatch(
            token,
            "normal",
            ManuscriptElementKind.Body,
            0.15m,
            candidates);
        AddStyleMatch(
            token,
            "caption",
            ManuscriptElementKind.FigureCaption,
            0.20m,
            candidates);
        AddStyleMatch(
            token,
            "caption",
            ManuscriptElementKind.TableCaption,
            0.20m,
            candidates);
        AddStyleMatch(
            token,
            "reference",
            ManuscriptElementKind.ReferenceEntry,
            0.30m,
            candidates);
    }

    private static void AddStyleMatch(
        string token,
        string marker,
        ManuscriptElementKind kind,
        decimal weight,
        IDictionary<ManuscriptElementKind, CandidateEvidence> candidates)
    {
        if (token.Contains(marker, StringComparison.Ordinal))
        {
            Add(
                candidates,
                kind,
                new ClassificationReason(
                    $"classification.style.{marker}",
                    ClassificationEvidenceKind.Style,
                    weight,
                    "The paragraph style carries a matching semantic marker."));
        }
    }

    private static void AddPatternEvidence(
        string text,
        IDictionary<ManuscriptElementKind, CandidateEvidence> candidates,
        bool suppressHeadingPatterns)
    {
        if (StartsWith(text, "Abstract") || StartsWith(text, "摘要"))
        {
            AddPattern(
                candidates,
                ManuscriptElementKind.Abstract,
                "abstract",
                0.75m);
        }

        if (StartsWith(text, "Index Terms")
            || StartsWith(text, "Keywords")
            || StartsWith(text, "关键词"))
        {
            AddPattern(
                candidates,
                ManuscriptElementKind.Keywords,
                "keywords",
                0.75m);
        }

        if (!suppressHeadingPatterns && LooksLikeHeading1(text))
        {
            AddPattern(
                candidates,
                ManuscriptElementKind.Heading1,
                "heading-1",
                0.75m);
        }

        if (!suppressHeadingPatterns && LooksLikeHeading2(text))
        {
            AddPattern(
                candidates,
                ManuscriptElementKind.Heading2,
                "heading-2",
                0.75m);
        }

        if (!suppressHeadingPatterns && LooksLikeHeading3(text))
        {
            AddPattern(
                candidates,
                ManuscriptElementKind.Heading3,
                "heading-3",
                0.75m);
        }

        if (IsFigureCaption(text))
        {
            AddPattern(
                candidates,
                ManuscriptElementKind.FigureCaption,
                "figure-caption",
                0.85m);
        }

        if (IsTableCaption(text))
        {
            AddPattern(
                candidates,
                ManuscriptElementKind.TableCaption,
                "table-caption",
                0.85m);
        }

        if (IsReferencesHeading(text))
        {
            AddPattern(
                candidates,
                ManuscriptElementKind.ReferencesHeading,
                "references-heading",
                0.90m);
        }

        if (LooksLikeReferenceEntry(text))
        {
            AddPattern(
                candidates,
                ManuscriptElementKind.ReferenceEntry,
                "reference-entry",
                0.65m);
        }

        if (text.Length >= 60)
        {
            Add(
                candidates,
                ManuscriptElementKind.Body,
                new ClassificationReason(
                    "classification.pattern.prose-length",
                    ClassificationEvidenceKind.TextPattern,
                    0.35m,
                    "The paragraph length is consistent with prose."));
        }
    }

    private static void AddPattern(
        IDictionary<ManuscriptElementKind, CandidateEvidence> candidates,
        ManuscriptElementKind kind,
        string code,
        decimal weight) =>
        Add(
            candidates,
            kind,
            new ClassificationReason(
                $"classification.pattern.{code}",
                ClassificationEvidenceKind.TextPattern,
                weight,
                "The paragraph matches a deterministic manuscript text pattern."));

    private static void AddFormattingEvidence(
        ParagraphModel paragraph,
        string text,
        IDictionary<ManuscriptElementKind, CandidateEvidence> candidates)
    {
        ParagraphAlignment? alignment =
            paragraph.EffectiveFormatting.Alignment;
        Twip? maximumFontSize = paragraph.Runs
            .Select(run => run.EffectiveFormatting.FontSize)
            .Where(size => size.HasValue)
            .Select(size => size.GetValueOrDefault())
            .DefaultIfEmpty()
            .Max();
        bool bold = paragraph.Runs.Any(
            run => run.EffectiveFormatting.Bold == true);
        bool italic = paragraph.Runs.Any(
            run => run.EffectiveFormatting.Italic == true);

        if (maximumFontSize is { Value: >= 360 })
        {
            AddFormatting(
                candidates,
                ManuscriptElementKind.Title,
                "large-font",
                0.25m);
        }

        if (alignment == ParagraphAlignment.Center)
        {
            AddFormatting(
                candidates,
                ManuscriptElementKind.Title,
                "centered",
                0.10m);
            AddFormatting(
                candidates,
                ManuscriptElementKind.Author,
                "centered",
                0.15m);
            AddFormatting(
                candidates,
                ManuscriptElementKind.Affiliation,
                "centered",
                0.15m);
            AddFormatting(
                candidates,
                ManuscriptElementKind.Heading1,
                "centered",
                0.10m);
            AddFormatting(
                candidates,
                ManuscriptElementKind.FigureCaption,
                "centered",
                0.10m);
            AddFormatting(
                candidates,
                ManuscriptElementKind.TableCaption,
                "centered",
                0.10m);
        }

        if (alignment == ParagraphAlignment.Justified)
        {
            AddFormatting(
                candidates,
                ManuscriptElementKind.Body,
                "justified",
                0.15m);
            AddFormatting(
                candidates,
                ManuscriptElementKind.ReferenceEntry,
                "justified",
                0.10m);
        }

        if (text.Length <= 140 && bold)
        {
            AddFormatting(
                candidates,
                ManuscriptElementKind.Heading1,
                "short-bold",
                0.15m);
            AddFormatting(
                candidates,
                ManuscriptElementKind.ReferencesHeading,
                "short-bold",
                0.10m);
        }

        if (text.Length <= 140 && italic)
        {
            AddFormatting(
                candidates,
                ManuscriptElementKind.Heading2,
                "short-italic",
                0.10m);
            AddFormatting(
                candidates,
                ManuscriptElementKind.Heading3,
                "short-italic",
                0.10m);
            AddFormatting(
                candidates,
                ManuscriptElementKind.Affiliation,
                "italic",
                0.10m);
        }

        if (paragraph.EffectiveFormatting.Indentation?.Hanging is not null)
        {
            AddFormatting(
                candidates,
                ManuscriptElementKind.ReferenceEntry,
                "hanging-indent",
                0.15m);
        }
    }

    private static void AddFormatting(
        IDictionary<ManuscriptElementKind, CandidateEvidence> candidates,
        ManuscriptElementKind kind,
        string code,
        decimal weight) =>
        Add(
            candidates,
            kind,
            new ClassificationReason(
                $"classification.format.{code}",
                ClassificationEvidenceKind.Formatting,
                weight,
                "The effective paragraph formatting supports this element type."));

    private static void AddPositionEvidence(
        int position,
        IDictionary<ManuscriptElementKind, CandidateEvidence> candidates)
    {
        if (position == 0)
        {
            Add(
                candidates,
                ManuscriptElementKind.Title,
                new ClassificationReason(
                    "classification.position.first",
                    ClassificationEvidenceKind.Position,
                    0.50m,
                    "The paragraph is the first non-empty document paragraph."));
        }
        else if (position == 1)
        {
            Add(
                candidates,
                ManuscriptElementKind.Author,
                new ClassificationReason(
                    "classification.position.second",
                    ClassificationEvidenceKind.Position,
                    0.35m,
                    "The paragraph follows the likely title."));
        }
        else if (position == 2)
        {
            Add(
                candidates,
                ManuscriptElementKind.Affiliation,
                new ClassificationReason(
                    "classification.position.third",
                    ClassificationEvidenceKind.Position,
                    0.35m,
                    "The paragraph follows the likely author line."));
        }
    }

    private static void AddContextEvidence(
        ParagraphModel paragraph,
        int position,
        int referencesIndex,
        ParagraphModel? previous,
        IDictionary<ManuscriptElementKind, CandidateEvidence> candidates)
    {
        if (referencesIndex >= 0 && position > referencesIndex)
        {
            Add(
                candidates,
                ManuscriptElementKind.ReferenceEntry,
                new ClassificationReason(
                    "classification.context.after-references",
                    ClassificationEvidenceKind.Context,
                    0.30m,
                    "The paragraph follows the references heading."));
        }

        if (previous is not null
            && (LooksLikeHeading1(previous.Text.Value)
                || LooksLikeHeading2(previous.Text.Value)
                || LooksLikeHeading3(previous.Text.Value))
            && paragraph.Text.Length >= 40)
        {
            Add(
                candidates,
                ManuscriptElementKind.Body,
                new ClassificationReason(
                    "classification.context.after-heading",
                    ClassificationEvidenceKind.Context,
                    0.10m,
                    "The prose paragraph follows a section heading."));
        }
    }

    private static void Add(
        IDictionary<ManuscriptElementKind, CandidateEvidence> candidates,
        ManuscriptElementKind kind,
        ClassificationReason reason)
    {
        if (!candidates.TryGetValue(kind, out CandidateEvidence? candidate))
        {
            candidate = new CandidateEvidence(kind);
            candidates.Add(kind, candidate);
        }

        candidate.Add(reason);
    }

    private static string NormalizeStyleToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static bool HasStyleMarker(
        ParagraphModel paragraph,
        IReadOnlyDictionary<string, StyleDefinition> styles,
        string marker)
    {
        if (paragraph.StyleId is null)
        {
            return false;
        }

        styles.TryGetValue(paragraph.StyleId, out StyleDefinition? style);
        string token = NormalizeStyleToken(
            paragraph.StyleId + " " + (style?.Name ?? string.Empty));
        return token.Contains(marker, StringComparison.Ordinal);
    }

    private static bool StartsWith(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsFigureCaption(string text) =>
        StartsWith(text.TrimStart(), "Fig.")
        || StartsWith(text.TrimStart(), "Figure")
        || text.TrimStart().StartsWith('图');

    private static bool IsTableCaption(string text) =>
        StartsWith(text.TrimStart(), "Table")
        || text.TrimStart().StartsWith('表');

    private static bool IsReferencesHeading(string text) =>
        string.Equals(text.Trim(), "References", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text.Trim(), "参考文献", StringComparison.Ordinal);

    private static bool LooksLikeReferenceEntry(string text)
    {
        string trimmed = text.TrimStart();
        return trimmed.Length >= 3
            && trimmed[0] == '['
            && char.IsDigit(trimmed[1])
            && trimmed.Contains(']');
    }

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

    private static string ElementId(StructuralLocation location) =>
        $"element:{location}";

    private sealed class CandidateEvidence
    {
        private readonly List<ClassificationReason> _reasons = [];

        public CandidateEvidence(ManuscriptElementKind kind)
        {
            Kind = kind;
        }

        public ManuscriptElementKind Kind { get; }

        public decimal Score { get; private set; }

        public IReadOnlyList<ClassificationReason> Reasons => _reasons;

        public void Add(ClassificationReason reason)
        {
            _reasons.Add(reason);
            Score += reason.Weight;
        }
    }
}
