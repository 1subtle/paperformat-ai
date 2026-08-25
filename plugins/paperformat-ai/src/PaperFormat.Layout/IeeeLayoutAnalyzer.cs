using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using PaperFormat.Domain;
using PaperFormat.OpenXml;
using M = DocumentFormat.OpenXml.Math;
using W = DocumentFormat.OpenXml.Wordprocessing;
using WP = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace PaperFormat.Layout;

/// <summary>
/// Produces content-safe evidence for a single-column to IEEE-style
/// front-matter/full-width and body/two-column conversion.
/// </summary>
public static class IeeeLayoutAnalyzer
{
    public static LayoutAnalysis Analyze(
        string sourcePath,
        RulePackage targetRules,
        ClassificationSet classifications)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(targetRules);
        ArgumentNullException.ThrowIfNull(classifications);
        DocumentParseResult parsed = WordDocumentParser.Parse(sourcePath);
        DocumentModel document = parsed.Document
            ?? throw new InvalidDataException(
                "The source DOCX could not be parsed for layout analysis.");
        int targetColumns = IntegerRule(
            targetRules,
            FormatProperty.ColumnCount,
            fallback: 2);
        int targetSpacing = TwipRule(
            targetRules,
            FormatProperty.ColumnSpacing,
            fallback: 360);
        DocumentElement[] mainElements = classifications.Elements
            .Where(
                item => item.Location.Part
                    == DocumentPartKind.MainDocument)
            .Where(item => item.Location.TableIndex is null)
            .OrderBy(item => item.Location)
            .ToArray();
        DocumentElement? frontEnd = mainElements
            .Where(
                item => item.Kind is
                    ManuscriptElementKind.Title
                    or ManuscriptElementKind.Author
                    or ManuscriptElementKind.Affiliation
                    or ManuscriptElementKind.Abstract
                    or ManuscriptElementKind.Keywords)
            .LastOrDefault();
        DocumentElement? bodyStart = mainElements
            .Where(
                item => item.Kind is
                    ManuscriptElementKind.Heading1
                    or ManuscriptElementKind.Body)
            .FirstOrDefault(
                item => frontEnd is null
                    || item.Location.CompareTo(frontEnd.Location) > 0);
        var blockers = new List<string>();
        int[] sourceColumns = document.Sections
            .Select(section => section.PageSettings.Columns.Count ?? 1)
            .ToArray();
        if (document.Sections.Count != 1)
        {
            blockers.Add(
                "The deterministic converter currently requires one source section; use an Experimental multi-section plan.");
        }

        if (sourceColumns.Any(count => count != 1))
        {
            blockers.Add(
                "The source is not a uniformly single-column document.");
        }

        if (targetColumns < 2)
        {
            blockers.Add(
                "The target rule package does not require a multi-column body.");
        }

        if (frontEnd is null || bodyStart is null)
        {
            blockers.Add(
                "A reliable front-matter/body boundary could not be identified.");
        }

        int columnWidth = TargetColumnWidth(
            document,
            targetRules,
            targetColumns,
            targetSpacing);
        LayoutRiskFinding[] risks = InspectRisks(
            sourcePath,
            columnWidth);
        return new LayoutAnalysis(
            Hash(sourcePath),
            document.Sections.Count,
            sourceColumns,
            targetColumns,
            targetSpacing,
            frontEnd?.ElementId,
            bodyStart?.ElementId,
            blockers.Count == 0,
            blockers,
            risks);
    }

    private static LayoutRiskFinding[] InspectRisks(
        string sourcePath,
        int targetColumnWidthTwips)
    {
        using WordprocessingDocument package =
            WordprocessingDocument.Open(sourcePath, false);
        W.Body body = package.MainDocumentPart?.Document?.Body
            ?? throw new InvalidDataException(
                "The source DOCX has no main document body.");
        var findings = new List<LayoutRiskFinding>();
        W.Table[] tables = body.Elements<W.Table>().ToArray();
        for (int tableIndex = 0;
             tableIndex < tables.Length;
             tableIndex++)
        {
            W.Table table = tables[tableIndex];
            var location = new StructuralLocation(
                DocumentPartKind.MainDocument,
                sectionIndex: 0,
                tableIndex: tableIndex);
            long? width = TableWidth(table);
            if (width is not null && width > targetColumnWidthTwips)
            {
                findings.Add(Finding(
                    LayoutRiskObjectKind.WideTable,
                    ModificationLevel.Review,
                    location,
                    "A table is wider than the target column and requires a layout decision."));
            }

            if (table.Descendants<W.GridSpan>().Any(
                    span => (span.Val?.Value ?? 1) > 1)
                || table.Descendants<W.VerticalMerge>().Any())
            {
                findings.Add(Finding(
                    LayoutRiskObjectKind.MergedTable,
                    ModificationLevel.Experimental,
                    location,
                    "A merged-cell table is protected from automatic geometry changes."));
            }
        }

        W.Paragraph[] paragraphs = body.Elements<W.Paragraph>().ToArray();
        for (int paragraphIndex = 0;
             paragraphIndex < paragraphs.Length;
             paragraphIndex++)
        {
            W.Paragraph paragraph = paragraphs[paragraphIndex];
            var location = new StructuralLocation(
                DocumentPartKind.MainDocument,
                sectionIndex: 0,
                paragraphIndex: paragraphIndex);
            foreach (WP.Inline inline in paragraph.Descendants<WP.Inline>())
            {
                long widthTwips =
                    (inline.Extent?.Cx?.Value ?? 0L) / 635L;
                if (widthTwips > targetColumnWidthTwips)
                {
                    findings.Add(Finding(
                        LayoutRiskObjectKind.InlineDrawing,
                        ModificationLevel.Review,
                        location,
                        "An inline drawing is wider than the target column."));
                }
            }

            if (paragraph.Descendants<WP.Anchor>().Any())
            {
                findings.Add(Finding(
                    LayoutRiskObjectKind.FloatingDrawing,
                    ModificationLevel.Experimental,
                    location,
                    "A floating drawing may move during reflow and is protected."));
            }

            if (paragraph.Descendants<M.OfficeMath>().Any()
                || paragraph.Descendants<M.Paragraph>().Any())
            {
                findings.Add(Finding(
                    LayoutRiskObjectKind.Equation,
                    ModificationLevel.Review,
                    location,
                    "An equation requires rendered-page width verification after column conversion."));
            }

            if (paragraph.Descendants<W.FieldCode>().Any())
            {
                findings.Add(Finding(
                    LayoutRiskObjectKind.Field,
                    ModificationLevel.Review,
                    location,
                    "A field is preserved and must be verified after pagination changes."));
            }
        }

        return findings
            .OrderBy(item => item.Location)
            .ThenBy(item => item.Kind)
            .ToArray();
    }

    private static LayoutRiskFinding Finding(
        LayoutRiskObjectKind kind,
        ModificationLevel level,
        StructuralLocation location,
        string message)
    {
        string id = Convert.ToHexString(
            SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    $"{kind}|{location.CanonicalPath}")))
            .ToLowerInvariant()[..20];
        return new LayoutRiskFinding(
            "layout-risk-" + id,
            kind,
            level,
            location,
            message);
    }

    private static int TargetColumnWidth(
        DocumentModel document,
        RulePackage rules,
        int count,
        int spacing)
    {
        PageSettings page = document.Sections[0].PageSettings;
        int width = TwipRule(
            rules,
            FormatProperty.PageWidth,
            ToInt(page.Width?.Value, 12_240));
        int left = TwipRule(
            rules,
            FormatProperty.MarginLeft,
            ToInt(page.Margins.Left?.Value, 720));
        int right = TwipRule(
            rules,
            FormatProperty.MarginRight,
            ToInt(page.Margins.Right?.Value, 720));
        return Math.Max(
            1,
            (width - left - right - (count - 1) * spacing) / count);
    }

    private static int IntegerRule(
        RulePackage rules,
        FormatProperty property,
        int fallback) =>
        rules.Rules
            .FirstOrDefault(
                item => item.Target == RuleTarget.Page
                    && item.Property == property)
            ?.Expected is IntegerRuleValue value
                ? value.Value
                : fallback;

    private static int TwipRule(
        RulePackage rules,
        FormatProperty property,
        int fallback) =>
        rules.Rules
            .FirstOrDefault(
                item => item.Target == RuleTarget.Page
                    && item.Property == property)
            ?.Expected is TwipRuleValue value
                ? checked((int)value.Value.Value)
                : fallback;

    private static long? TableWidth(W.Table table)
    {
        W.TableWidth? width = table.TableProperties?.TableWidth;
        if (width is null)
        {
            return null;
        }

        return width.Type?.Value == W.TableWidthUnitValues.Dxa
            && long.TryParse(width.Width?.Value, out long value)
                ? value
                : null;
    }

    private static int ToInt(long? value, int fallback) =>
        value is null ? fallback : checked((int)value.Value);

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
    }
}
