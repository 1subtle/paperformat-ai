using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using PaperFormat.Domain;

namespace PaperFormat.OpenXml;

/// <summary>
/// Stable diagnostic codes emitted by <see cref="WordDocumentParser"/>.
/// </summary>
public static class WordDocumentParserDiagnosticCodes
{
    public const string PackageOpenFailed = "parser.package.open_failed";
    public const string MainDocumentPartMissing = "parser.main_part.missing";
    public const string DocumentBodyMissing = "parser.body.missing";
    public const string StylesPartMissing = "parser.styles.missing";
    public const string MissingStyleId = "parser.style.id_missing";
    public const string DuplicateStyleId = "parser.style.id_duplicate";
    public const string MissingBaseStyle = "parser.style.base_missing";
    public const string MissingLinkedStyle = "parser.style.link_missing";
    public const string StyleInheritanceCycle = "parser.style.cycle";
    public const string UndefinedStyleReference = "parser.style.undefined";
    public const string SectionPropertiesMissing = "parser.section.properties_missing";
}

/// <summary>
/// Parses a preflighted DOCX or DOTX package into the immutable domain model.
/// Packages are opened read-only and diagnostics never contain manuscript text.
/// </summary>
public static class WordDocumentParser
{
    public static DocumentParseResult Parse(
        string path,
        PackagePreflightOptions? options = null)
    {
        PackagePreflightResult preflight =
            DocxPackagePreflight.Inspect(path, options);
        if (!preflight.IsValid)
        {
            return PreflightFailure(preflight);
        }

        try
        {
            using WordprocessingDocument package =
                WordprocessingDocument.Open(path, false);
            return ParsePackage(package, PackageKind(path));
        }
        catch (Exception exception) when (IsPackageReadFailure(exception))
        {
            return OpenFailure();
        }
    }

    public static DocumentParseResult Parse(
        Stream packageStream,
        string fileName,
        PackagePreflightOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        PackagePreflightOptions effectiveOptions =
            options ?? new PackagePreflightOptions();
        MemoryStream? buffered = Buffer(
            packageStream,
            effectiveOptions.MaxPackageBytes);
        if (buffered is null)
        {
            return DocumentParseResult.Failure(
            [
                new ParseDiagnostic(
                    "preflight.PackageTooLarge",
                    ParseDiagnosticSeverity.Error,
                    "The ZIP package exceeds the compressed-size limit."),
            ]);
        }

        using (buffered)
        {
            PackagePreflightResult preflight =
                DocxPackagePreflight.Inspect(
                    buffered,
                    fileName,
                    effectiveOptions);
            if (!preflight.IsValid)
            {
                return PreflightFailure(preflight);
            }

            try
            {
                buffered.Position = 0;
                using WordprocessingDocument package =
                    WordprocessingDocument.Open(buffered, false);
                return ParsePackage(package, PackageKind(fileName));
            }
            catch (Exception exception) when (IsPackageReadFailure(exception))
            {
                return OpenFailure();
            }
        }
    }

    private static DocumentParseResult ParsePackage(
        WordprocessingDocument package,
        WordPackageKind packageKind)
    {
        try
        {
            MainDocumentPart? mainPart = package.MainDocumentPart;
            if (mainPart is null)
            {
                return Failure(
                    WordDocumentParserDiagnosticCodes.MainDocumentPartMissing,
                    "The package has no readable main document part.");
            }

            OpenXmlElement? document = mainPart.Document;
            OpenXmlElement? body = OpenXmlValueReader.Child(document, "body");
            if (body is null)
            {
                return Failure(
                    WordDocumentParserDiagnosticCodes.DocumentBodyMissing,
                    "The main document part has no readable body.");
            }

            List<ParseDiagnostic> diagnostics = [];
            OpenXmlElement? stylesRoot =
                mainPart.StyleDefinitionsPart?.Styles;
            StyleResolver styles =
                StyleResolver.Create(stylesRoot, diagnostics);
            List<SectionModel> sections =
                ParseSections(body, styles, diagnostics);

            var model = new DocumentModel(
                packageKind,
                styles.Defaults,
                styles.Styles,
                sections);
            return DocumentParseResult.Success(model, diagnostics);
        }
        catch (Exception exception) when (IsPackageReadFailure(exception))
        {
            return OpenFailure();
        }
    }

    private static List<SectionModel> ParseSections(
        OpenXmlElement body,
        StyleResolver styles,
        List<ParseDiagnostic> diagnostics)
    {
        List<SectionModel> sections = [];
        List<ParagraphModel> paragraphs = [];
        List<TableModel> tables = [];
        int sectionIndex = 0;
        int paragraphIndex = 0;
        int tableIndex = 0;
        int blockIndex = 0;

        foreach (OpenXmlElement block in body.ChildElements)
        {
            if (string.Equals(block.LocalName, "p", StringComparison.Ordinal))
            {
                OpenXmlElement? paragraphProperties =
                    OpenXmlValueReader.Child(block, "pPr");
                paragraphs.Add(ParseParagraph(
                    block,
                    new StructuralLocation(
                        DocumentPartKind.MainDocument,
                        sectionIndex: sectionIndex,
                        paragraphIndex: paragraphIndex++),
                    blockIndex++,
                    tableStyleId: null,
                    styles));

                OpenXmlElement? sectionProperties =
                    OpenXmlValueReader.Child(paragraphProperties, "sectPr");
                if (sectionProperties is not null)
                {
                    sections.Add(CreateSection(
                        sectionIndex++,
                        sectionProperties,
                        paragraphs,
                        tables));
                    paragraphs = [];
                    tables = [];
                    paragraphIndex = 0;
                    tableIndex = 0;
                    blockIndex = 0;
                }
            }
            else if (string.Equals(
                         block.LocalName,
                         "tbl",
                         StringComparison.Ordinal))
            {
                tables.Add(ParseTable(
                    block,
                    sectionIndex,
                    tableIndex++,
                    blockIndex++,
                    styles));
            }
        }

        OpenXmlElement? finalSectionProperties =
            OpenXmlValueReader.Child(body, "sectPr");
        if (paragraphs.Count > 0
            || tables.Count > 0
            || sections.Count == 0
            || finalSectionProperties is not null)
        {
            sections.Add(CreateSection(
                sectionIndex,
                finalSectionProperties,
                paragraphs,
                tables));
        }

        if (sections.Any(
                section => section.PageSettings == PageSettings.Empty))
        {
            diagnostics.Add(new ParseDiagnostic(
                WordDocumentParserDiagnosticCodes.SectionPropertiesMissing,
                ParseDiagnosticSeverity.Warning,
                "A section has no explicit page settings."));
        }

        return sections;
    }

    private static SectionModel CreateSection(
        int sectionIndex,
        OpenXmlElement? sectionProperties,
        IEnumerable<ParagraphModel> paragraphs,
        IEnumerable<TableModel> tables) =>
        new(
            new StructuralLocation(
                DocumentPartKind.MainDocument,
                sectionIndex: sectionIndex),
            FormattingResolver.ReadPageSettings(sectionProperties),
            paragraphs,
            tables);

    private static ParagraphModel ParseParagraph(
        OpenXmlElement paragraph,
        StructuralLocation location,
        int blockIndex,
        string? tableStyleId,
        StyleResolver styles)
    {
        OpenXmlElement? properties =
            OpenXmlValueReader.Child(paragraph, "pPr");
        string? styleId =
            OpenXmlValueReader.ChildValue(properties, "pStyle");
        ParagraphFormatting direct =
            FormattingResolver.ReadParagraph(properties);
        CharacterFormatting paragraphMarkDirect =
            FormattingResolver.ReadCharacter(
                OpenXmlValueReader.Child(properties, "rPr"));

        List<RunModel> runs = [];
        int runIndex = 0;
        foreach (OpenXmlElement run in DescendantRuns(paragraph))
        {
            OpenXmlElement? runProperties =
                OpenXmlValueReader.Child(run, "rPr");
            string? runStyleId =
                OpenXmlValueReader.ChildValue(runProperties, "rStyle");
            CharacterFormatting runDirect =
                FormattingResolver.ReadCharacter(runProperties);
            runs.Add(new RunModel(
                new StructuralLocation(
                    part: location.Part,
                    partIndex: location.PartIndex,
                    sectionIndex: location.SectionIndex,
                    paragraphIndex: location.ParagraphIndex,
                    tableIndex: location.TableIndex,
                    rowIndex: location.RowIndex,
                    cellIndex: location.CellIndex,
                    runIndex: runIndex++),
                runStyleId,
                new DocumentText(run.InnerText),
                runDirect,
                styles.ResolveCharacter(
                    styleId,
                    tableStyleId,
                    paragraphMarkDirect,
                    runStyleId,
                    runDirect)));
        }

        return new ParagraphModel(
            location,
            blockIndex,
            styleId,
            new DocumentText(paragraph.InnerText),
            direct,
            styles.ResolveParagraph(styleId, tableStyleId, direct),
            runs);
    }

    private static TableModel ParseTable(
        OpenXmlElement table,
        int sectionIndex,
        int tableIndex,
        int blockIndex,
        StyleResolver styles)
    {
        OpenXmlElement? properties =
            OpenXmlValueReader.Child(table, "tblPr");
        string? styleId =
            OpenXmlValueReader.ChildValue(properties, "tblStyle");
        TableFormatting direct =
            FormattingResolver.ReadTable(properties);
        StructuralLocation tableLocation = new(
            DocumentPartKind.MainDocument,
            sectionIndex: sectionIndex,
            tableIndex: tableIndex);

        List<RowModel> rows = [];
        int rowIndex = 0;
        foreach (OpenXmlElement row in
                 OpenXmlValueReader.Children(table, "tr"))
        {
            OpenXmlElement? rowProperties =
                OpenXmlValueReader.Child(row, "trPr");
            RowFormatting rowDirect =
                FormattingResolver.ReadRow(rowProperties);
            StructuralLocation rowLocation = new(
                DocumentPartKind.MainDocument,
                sectionIndex: sectionIndex,
                tableIndex: tableIndex,
                rowIndex: rowIndex);

            List<CellModel> cells = [];
            int cellIndex = 0;
            foreach (OpenXmlElement cell in
                     OpenXmlValueReader.Children(row, "tc"))
            {
                OpenXmlElement? cellProperties =
                    OpenXmlValueReader.Child(cell, "tcPr");
                CellFormatting cellDirect =
                    FormattingResolver.ReadCell(cellProperties);
                StructuralLocation cellLocation = new(
                    DocumentPartKind.MainDocument,
                    sectionIndex: sectionIndex,
                    tableIndex: tableIndex,
                    rowIndex: rowIndex,
                    cellIndex: cellIndex);

                List<ParagraphModel> cellParagraphs = [];
                int cellParagraphIndex = 0;
                int cellBlockIndex = 0;
                foreach (OpenXmlElement cellBlock in cell.ChildElements)
                {
                    if (string.Equals(
                            cellBlock.LocalName,
                            "p",
                            StringComparison.Ordinal))
                    {
                        cellParagraphs.Add(ParseParagraph(
                            cellBlock,
                            new StructuralLocation(
                                DocumentPartKind.MainDocument,
                                sectionIndex: sectionIndex,
                                paragraphIndex: cellParagraphIndex++,
                                tableIndex: tableIndex,
                                rowIndex: rowIndex,
                                cellIndex: cellIndex),
                            cellBlockIndex,
                            styleId,
                            styles));
                    }

                    if (cellBlock.LocalName is "p" or "tbl")
                    {
                        cellBlockIndex++;
                    }
                }

                int gridSpan =
                    OpenXmlValueReader.PositiveIntAttribute(
                        OpenXmlValueReader.Child(cellProperties, "gridSpan"),
                        "val")
                    ?? 1;
                cells.Add(new CellModel(
                    cellLocation,
                    gridSpan,
                    cellDirect,
                    cellDirect,
                    cellParagraphs));
                cellIndex++;
            }

            rows.Add(new RowModel(
                rowLocation,
                rowDirect,
                rowDirect,
                cells));
            rowIndex++;
        }

        return new TableModel(
            tableLocation,
            blockIndex,
            styleId,
            direct,
            styles.ResolveTable(styleId, direct),
            rows);
    }

    private static IEnumerable<OpenXmlElement> DescendantRuns(
        OpenXmlElement paragraph)
    {
        foreach (OpenXmlElement child in paragraph.ChildElements)
        {
            // A paragraph can contain both WordprocessingML runs (<w:r>) and
            // Office Math runs (<m:r>). Only <w:r> participates in the
            // structural run index used by the deterministic repair engine.
            if (child is DocumentFormat.OpenXml.Wordprocessing.Run)
            {
                yield return child;
                continue;
            }

            if (string.Equals(child.LocalName, "p", StringComparison.Ordinal)
                || string.Equals(
                    child.LocalName,
                    "tbl",
                    StringComparison.Ordinal))
            {
                continue;
            }

            foreach (OpenXmlElement run in DescendantRuns(child))
            {
                yield return run;
            }
        }
    }

    private static MemoryStream? Buffer(Stream source, long maxBytes)
    {
        long? originalPosition = source.CanSeek ? source.Position : null;
        MemoryStream destination = new();
        byte[] buffer = new byte[81920];

        try
        {
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (maxBytes <= 0 || destination.Length > maxBytes - read)
                {
                    destination.Dispose();
                    return null;
                }

                destination.Write(buffer, 0, read);
            }

            destination.Position = 0;
            return destination;
        }
        catch (IOException)
        {
            destination.Dispose();
            return null;
        }
        finally
        {
            if (originalPosition is not null)
            {
                source.Position = originalPosition.Value;
            }
        }
    }

    private static bool IsPackageReadFailure(Exception exception) =>
        exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException
            or OpenXmlPackageException;

    private static WordPackageKind PackageKind(string fileName) =>
        string.Equals(
            Path.GetExtension(fileName),
            ".dotx",
            StringComparison.OrdinalIgnoreCase)
            ? WordPackageKind.Template
            : WordPackageKind.Document;

    private static DocumentParseResult PreflightFailure(
        PackagePreflightResult preflight) =>
        DocumentParseResult.Failure(
            preflight.Diagnostics.Select(
                diagnostic => new ParseDiagnostic(
                    $"preflight.{diagnostic.Code}",
                    ParseDiagnosticSeverity.Error,
                    diagnostic.Message)));

    private static DocumentParseResult OpenFailure() =>
        Failure(
            WordDocumentParserDiagnosticCodes.PackageOpenFailed,
            "The OOXML package could not be opened for read-only parsing.");

    private static DocumentParseResult Failure(string code, string message) =>
        DocumentParseResult.Failure(
        [
            new ParseDiagnostic(
                code,
                ParseDiagnosticSeverity.Error,
                message),
        ]);
}
