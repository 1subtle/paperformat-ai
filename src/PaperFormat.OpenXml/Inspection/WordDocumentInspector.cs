using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using M = DocumentFormat.OpenXml.Math;
using PaperFormat.Domain;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace PaperFormat.OpenXml;

/// <summary>
/// Builds a content-safe structural inventory for Agent planning.
/// </summary>
public static class WordDocumentInspector
{
    public static DocumentInspection Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        DocumentParseResult parse = WordDocumentParser.Parse(fullPath);
        DocumentModel document = parse.Document
            ?? throw new InvalidDataException(
                "The Word package could not be inspected.");

        using WordprocessingDocument package =
            WordprocessingDocument.Open(fullPath, false);
        MainDocumentPart main = package.MainDocumentPart
            ?? throw new InvalidDataException(
                "The Word package has no main document part.");
        W.Document wordDocument = main.Document
            ?? throw new InvalidDataException(
                "The Word package has no document root.");

        int paragraphCount = document.Sections.Sum(
            section => section.Paragraphs.Count
                + section.Tables.Sum(
                    table => table.Rows.Sum(
                        row => row.Cells.Sum(
                            cell => cell.Paragraphs.Count))));
        int runCount = document.Sections.Sum(
            section => section.Paragraphs.Sum(
                    paragraph => paragraph.Runs.Count)
                + section.Tables.Sum(
                    table => table.Rows.Sum(
                        row => row.Cells.Sum(
                            cell => cell.Paragraphs.Sum(
                                paragraph => paragraph.Runs.Count)))));
        int tableCount = document.Sections.Sum(
            section => section.Tables.Count);
        int fieldCount = wordDocument.Descendants<W.SimpleField>().Count()
            + wordDocument.Descendants<W.FieldChar>().Count(
                item => item.FieldCharType?.Value == W.FieldCharValues.Begin);

        var resources = new DocumentResourceInventory(
            paragraphCount,
            runCount,
            tableCount,
            main.ImageParts.Count(),
            wordDocument.Descendants<M.OfficeMath>().Count(),
            wordDocument.Descendants<W.Hyperlink>().Count(),
            wordDocument.Descendants<W.BookmarkStart>().Count(),
            fieldCount,
            main.FootnotesPart?.Footnotes?
                .Elements<W.Footnote>()
                .Count(item => item.Id?.Value >= 0) ?? 0,
            main.EndnotesPart?.Endnotes?
                .Elements<W.Endnote>()
                .Count(item => item.Id?.Value >= 0) ?? 0);
        SectionInspection[] sections = document.Sections
            .Select(
                (section, index) => new SectionInspection(
                    index,
                    section.PageSettings,
                    section.Paragraphs.Count,
                    section.Tables.Count))
            .ToArray();

        return new DocumentInspection(
            FileHash(fullPath),
            document.PackageKind,
            document.Styles.Count,
            resources,
            sections,
            parse.Diagnostics);
    }

    private static string FileHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
    }
}
