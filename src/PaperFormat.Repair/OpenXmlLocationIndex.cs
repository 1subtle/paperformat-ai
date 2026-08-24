using DocumentFormat.OpenXml;
using PaperFormat.Domain;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace PaperFormat.Repair;

internal sealed class OpenXmlLocationIndex
{
    private readonly Dictionary<string, OpenXmlElement> _elements =
        new(StringComparer.Ordinal);

    public OpenXmlLocationIndex(W.Body body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Build(body);
    }

    public T? Find<T>(StructuralLocation location)
        where T : OpenXmlElement =>
        _elements.TryGetValue(
            location.CanonicalPath,
            out OpenXmlElement? element)
            ? element as T
            : null;

    private void Build(W.Body body)
    {
        int sectionIndex = 0;
        int paragraphIndex = 0;
        int tableIndex = 0;

        foreach (OpenXmlElement block in body.ChildElements)
        {
            if (block is W.Paragraph paragraph)
            {
                IndexParagraph(
                    paragraph,
                    new StructuralLocation(
                        DocumentPartKind.MainDocument,
                        sectionIndex: sectionIndex,
                        paragraphIndex: paragraphIndex++));
                W.SectionProperties? sectionProperties =
                    paragraph.ParagraphProperties?
                        .GetFirstChild<W.SectionProperties>();
                if (sectionProperties is not null)
                {
                    IndexSection(sectionIndex, sectionProperties);
                    sectionIndex++;
                    paragraphIndex = 0;
                    tableIndex = 0;
                }
            }
            else if (block is W.Table table)
            {
                IndexTable(table, sectionIndex, tableIndex++);
            }
        }

        W.SectionProperties? finalSection =
            body.GetFirstChild<W.SectionProperties>();
        if (finalSection is not null)
        {
            IndexSection(sectionIndex, finalSection);
        }
    }

    private void IndexSection(
        int sectionIndex,
        W.SectionProperties properties) =>
        _elements.Add(
            new StructuralLocation(
                DocumentPartKind.MainDocument,
                sectionIndex: sectionIndex).CanonicalPath,
            properties);

    private void IndexTable(
        W.Table table,
        int sectionIndex,
        int tableIndex)
    {
        int rowIndex = 0;
        foreach (W.TableRow row in table.Elements<W.TableRow>())
        {
            int cellIndex = 0;
            foreach (W.TableCell cell in row.Elements<W.TableCell>())
            {
                int paragraphIndex = 0;
                foreach (OpenXmlElement block in cell.ChildElements)
                {
                    if (block is W.Paragraph paragraph)
                    {
                        IndexParagraph(
                            paragraph,
                            new StructuralLocation(
                                DocumentPartKind.MainDocument,
                                sectionIndex: sectionIndex,
                                paragraphIndex: paragraphIndex++,
                                tableIndex: tableIndex,
                                rowIndex: rowIndex,
                                cellIndex: cellIndex));
                    }
                }

                cellIndex++;
            }

            rowIndex++;
        }
    }

    private void IndexParagraph(
        W.Paragraph paragraph,
        StructuralLocation location)
    {
        _elements.Add(location.CanonicalPath, paragraph);
        int runIndex = 0;
        foreach (W.Run run in DescendantRuns(paragraph))
        {
            _elements.Add(
                new StructuralLocation(
                    location.Part,
                    partIndex: location.PartIndex,
                    sectionIndex: location.SectionIndex,
                    paragraphIndex: location.ParagraphIndex,
                    tableIndex: location.TableIndex,
                    rowIndex: location.RowIndex,
                    cellIndex: location.CellIndex,
                    runIndex: runIndex++).CanonicalPath,
                run);
        }
    }

    private static IEnumerable<W.Run> DescendantRuns(OpenXmlElement parent)
    {
        foreach (OpenXmlElement child in parent.ChildElements)
        {
            if (child is W.Run run)
            {
                yield return run;
                continue;
            }

            if (child is W.Paragraph or W.Table)
            {
                continue;
            }

            foreach (W.Run descendant in DescendantRuns(child))
            {
                yield return descendant;
            }
        }
    }
}
