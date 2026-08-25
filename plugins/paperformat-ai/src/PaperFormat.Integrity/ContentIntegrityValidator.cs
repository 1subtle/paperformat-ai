using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using PaperFormat.Domain;

namespace PaperFormat.Integrity;

/// <summary>
/// Compares content-bearing DOCX features without exposing manuscript text.
/// </summary>
public static partial class ContentIntegrityValidator
{
    private static readonly XNamespace Word =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Relationships =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Math =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";

    public static IntegrityReport Compare(
        string sourcePath,
        string outputPath,
        IEnumerable<string>? approvedStructuralChanges = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        ContentSnapshot source = Capture(sourcePath);
        ContentSnapshot output = Capture(outputPath);
        HashSet<string> approved = (
            approvedStructuralChanges ?? Array.Empty<string>())
            .ToHashSet(StringComparer.Ordinal);
        IntegrityCheck[] checks = source.Items
            .Join(
                output.Items,
                item => item.CheckId,
                item => item.CheckId,
                (sourceItem, outputItem) => CompareItem(
                    sourceItem,
                    outputItem,
                    approved.Contains(sourceItem.CheckId)),
                StringComparer.Ordinal)
            .OrderBy(check => check.CheckId, StringComparer.Ordinal)
            .ToArray();
        IntegrityStatus status = checks.Any(
            check => check.Status == IntegrityStatus.Failed)
            ? IntegrityStatus.Failed
            : checks.Any(
                check => check.Status == IntegrityStatus.NeedsConfirmation)
                ? IntegrityStatus.NeedsConfirmation
                : IntegrityStatus.Passed;
        string reportId = Id(
            "integrity-v1",
            string.Join(
                "|",
                checks.Select(
                    check =>
                        $"{check.CheckId}:{check.Status}:" +
                        $"{check.SourceSha256}:{check.OutputSha256}")));
        return new IntegrityReport(reportId, status, checks);
    }

    private static ContentSnapshot Capture(string path)
    {
        using WordprocessingDocument package =
            WordprocessingDocument.Open(path, false);
        MainDocumentPart main = package.MainDocumentPart
            ?? throw new InvalidDataException(
                "The package has no main document part.");
        XDocument document = Load(main);
        XElement body = document.Root?.Element(Word + "body")
            ?? throw new InvalidDataException(
                "The main document has no body.");
        List<SnapshotItem> items =
        [
            Item(
                "normalized_body_text",
                TextNodes(body).ToArray()),
            Item(
                "paragraph_sequence",
                body.Descendants(Word + "p")
                    .Select(ParagraphText)
                    .ToArray()),
            Item(
                "tables",
                body.Descendants(Word + "tbl")
                    .Select(TableSignature)
                    .ToArray()),
            Item(
                "table_geometry",
                TableGeometrySignatures(body, main).ToArray()),
            Item(
                "effective_numbering",
                EffectiveNumberingSignatures(body, main).ToArray()),
            PartXmlItem(
                "numbering_definitions",
                main.NumberingDefinitionsPart is null
                    ? Array.Empty<OpenXmlPart>()
                    : [main.NumberingDefinitionsPart]),
            Item(
                "section_topology",
                SectionTopologySignatures(body).ToArray()),
            Item(
                "media",
                MediaSignatures(main).ToArray()),
            Item(
                "equations",
                body.Descendants(Math + "oMath")
                    .Select(EquationSignature)
                    .ToArray()),
            Item(
                "hyperlinks",
                body.Descendants(Word + "hyperlink")
                    .Select(element => HyperlinkSignature(element, main))
                    .ToArray()),
            Item(
                "bookmarks",
                BookmarkSignatures(body).ToArray()),
            Item(
                "fields",
                FieldSignatures(body).ToArray()),
            NoteItem("footnotes", main.FootnotesPart),
            NoteItem("endnotes", main.EndnotesPart),
            PartXmlItem(
                "headers",
                main.HeaderParts.Cast<OpenXmlPart>()),
            PartXmlItem(
                "footers",
                main.FooterParts.Cast<OpenXmlPart>()),
            PartXmlItem(
                "comments",
                main.WordprocessingCommentsPart is null
                    ? Array.Empty<OpenXmlPart>()
                    : [main.WordprocessingCommentsPart]),
            Item(
                "revisions",
                RevisionSignatures(body).ToArray()),
        ];
        return new ContentSnapshot(items);
    }

    private static IntegrityCheck CompareItem(
        SnapshotItem source,
        SnapshotItem output,
        bool approvedChange)
    {
        bool equal = source.Count == output.Count
            && string.Equals(
                source.Sha256,
                output.Sha256,
                StringComparison.Ordinal);
        bool passed = equal || approvedChange;
        return new IntegrityCheck(
            source.CheckId,
            passed ? IntegrityStatus.Passed : IntegrityStatus.Failed,
            source.Count,
            output.Count,
            source.Sha256,
            output.Sha256,
            equal
                ? "The content-bearing feature is unchanged."
                : approvedChange
                    ? "The structural feature changed exactly within an approved plan allowance."
                : "The content-bearing feature changed unexpectedly.");
    }

    private static SnapshotItem NoteItem(string checkId, OpenXmlPart? part)
    {
        if (part is null)
        {
            return Item(checkId, Array.Empty<string>());
        }

        XDocument document = Load(part);
        string singular = checkId == "footnotes" ? "footnote" : "endnote";
        string[] values = document
            .Descendants(Word + singular)
            .Select(
                note =>
                    Attribute(note, Word + "id") + ":" +
                    Normalize(string.Concat(TextNodes(note))))
            .ToArray();
        return Item(checkId, values);
    }

    private static SnapshotItem PartXmlItem(
        string checkId,
        IEnumerable<OpenXmlPart> parts)
    {
        string[] values = parts
            .OrderBy(part => part.Uri.ToString(), StringComparer.Ordinal)
            .Select(
                part =>
                    $"{part.Uri}:{CanonicalXml(Load(part).Root!)}")
            .ToArray();
        return Item(checkId, values);
    }

    private static IEnumerable<string> TextNodes(XElement root) =>
        root.Descendants()
            .Where(
                element => element.Name == Word + "t"
                    || element.Name == Math + "t")
            .Select(element => Normalize(element.Value));

    private static string ParagraphText(XElement paragraph) =>
        Normalize(string.Concat(TextNodes(paragraph)));

    private static string TableSignature(XElement table)
    {
        string[] rows = table.Elements(Word + "tr")
            .Select(
                row => EncodeSequence(
                    row.Elements(Word + "tc")
                        .Select(
                            cell => Normalize(
                                string.Concat(TextNodes(cell))))))
            .ToArray();
        return EncodeSequence(rows);
    }

    private static IEnumerable<string> TableGeometrySignatures(
        XElement body,
        MainDocumentPart main)
    {
        Dictionary<string, XElement> styleParagraphProperties =
            StyleParagraphProperties(main);
        HashSet<XName> tableProperties =
        [
            Word + "tblStyle",
            Word + "tblW",
            Word + "tblInd",
            Word + "tblLayout",
            Word + "tblCellMar",
            Word + "tblBorders",
            Word + "jc",
            Word + "tblLook",
        ];
        HashSet<XName> rowProperties =
        [
            Word + "gridBefore",
            Word + "gridAfter",
            Word + "wBefore",
            Word + "wAfter",
            Word + "trHeight",
            Word + "cantSplit",
            Word + "tblHeader",
        ];
        HashSet<XName> cellProperties =
        [
            Word + "tcW",
            Word + "gridSpan",
            Word + "vMerge",
            Word + "tcBorders",
            Word + "tcMar",
            Word + "vAlign",
            Word + "hideMark",
        ];

        foreach (XElement table in body.Descendants(Word + "tbl"))
        {
            List<string> parts = [];
            XElement? properties = table.Element(Word + "tblPr");
            parts.Add(
                EncodeSequence(
                    properties?
                        .Elements()
                        .Where(item => tableProperties.Contains(item.Name))
                        .Select(StructuralXml)
                    ?? Array.Empty<string>()));
            parts.Add(
                table.Element(Word + "tblGrid") is { } grid
                    ? StructuralXml(grid)
                    : string.Empty);
            foreach (XElement row in table.Elements(Word + "tr"))
            {
                parts.Add(
                    EncodeSequence(
                        row.Element(Word + "trPr")?
                            .Elements()
                            .Where(item => rowProperties.Contains(item.Name))
                            .Select(StructuralXml)
                        ?? Array.Empty<string>()));
                foreach (XElement cell in row.Elements(Word + "tc"))
                {
                    parts.Add(
                        EncodeSequence(
                            cell.Element(Word + "tcPr")?
                                .Elements()
                                .Where(
                                    item => cellProperties.Contains(item.Name))
                                .Select(StructuralXml)
                            ?? Array.Empty<string>()));
                    foreach (XElement paragraph in
                             cell.Descendants(Word + "p"))
                    {
                        parts.Add(
                            EffectiveParagraphBorder(
                                paragraph,
                                styleParagraphProperties));
                    }
                }
            }

            yield return EncodeSequence(parts);
        }
    }

    private static IEnumerable<string> EffectiveNumberingSignatures(
        XElement body,
        MainDocumentPart main)
    {
        Dictionary<string, StyleNumbering> styles = StyleNumberingMap(main);
        int index = 0;
        foreach (XElement paragraph in body.Descendants(Word + "p"))
        {
            XElement? paragraphProperties =
                paragraph.Element(Word + "pPr");
            XElement? number = paragraphProperties?.Element(Word + "numPr");
            string numId = Attribute(
                number?.Element(Word + "numId") ?? new XElement("empty"),
                Word + "val");
            string level = Attribute(
                number?.Element(Word + "ilvl") ?? new XElement("empty"),
                Word + "val");
            if (string.IsNullOrEmpty(numId))
            {
                string styleId = Attribute(
                    paragraphProperties?.Element(Word + "pStyle")
                        ?? new XElement("empty"),
                    Word + "val");
                StyleNumbering inherited = ResolveStyleNumbering(
                    styleId,
                    styles,
                    new HashSet<string>(StringComparer.Ordinal));
                numId = inherited.NumId;
                level = inherited.Level;
            }

            yield return EncodeSequence(
            [
                index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                numId,
                level,
            ]);
            index++;
        }
    }

    private static IEnumerable<string> SectionTopologySignatures(
        XElement body)
    {
        foreach (XElement section in body.Descendants(Word + "sectPr"))
        {
            XElement? columns = section.Element(Word + "cols");
            yield return EncodeSequence(
            [
                Attribute(
                    section.Element(Word + "type")
                        ?? new XElement("empty"),
                    Word + "val"),
                Attribute(columns ?? new XElement("empty"), Word + "num"),
                Attribute(columns ?? new XElement("empty"), Word + "equalWidth"),
                Attribute(columns ?? new XElement("empty"), Word + "sep"),
                (columns?.Elements(Word + "col").Count() ?? 0).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            ]);
        }
    }

    private static Dictionary<string, XElement> StyleParagraphProperties(
        MainDocumentPart main)
    {
        if (main.StyleDefinitionsPart is null)
        {
            return new Dictionary<string, XElement>(StringComparer.Ordinal);
        }

        XDocument styles = Load(main.StyleDefinitionsPart);
        return styles.Descendants(Word + "style")
            .Where(style => !string.IsNullOrEmpty(Attribute(style, Word + "styleId")))
            .ToDictionary(
                style => Attribute(style, Word + "styleId"),
                style => style.Element(Word + "pPr")
                    is { } paragraphProperties
                        ? new XElement(paragraphProperties)
                        : new XElement(Word + "pPr"),
                StringComparer.Ordinal);
    }

    private static string EffectiveParagraphBorder(
        XElement paragraph,
        Dictionary<string, XElement> styleParagraphProperties)
    {
        XElement? direct = paragraph.Element(Word + "pPr")
            ?.Element(Word + "pBdr");
        if (direct is not null)
        {
            return StructuralXml(direct);
        }

        string styleId = Attribute(
            paragraph.Element(Word + "pPr")?.Element(Word + "pStyle")
                ?? new XElement("empty"),
            Word + "val");
        return styleParagraphProperties.TryGetValue(
                styleId,
                out XElement? properties)
            && properties.Element(Word + "pBdr") is { } inherited
                ? StructuralXml(inherited)
                : string.Empty;
    }

    private static Dictionary<string, StyleNumbering> StyleNumberingMap(
        MainDocumentPart main)
    {
        if (main.StyleDefinitionsPart is null)
        {
            return new Dictionary<string, StyleNumbering>(
                StringComparer.Ordinal);
        }

        XDocument document = Load(main.StyleDefinitionsPart);
        return document.Descendants(Word + "style")
            .Where(style => !string.IsNullOrEmpty(Attribute(style, Word + "styleId")))
            .ToDictionary(
                style =>
                    Attribute(style, Word + "styleId"),
                style =>
                {
                    XElement? numPr = style.Element(Word + "pPr")
                        ?.Element(Word + "numPr");
                    return new StyleNumbering(
                        Attribute(
                            style.Element(Word + "basedOn")
                                ?? new XElement("empty"),
                            Word + "val"),
                        Attribute(
                            numPr?.Element(Word + "numId")
                                ?? new XElement("empty"),
                            Word + "val"),
                        Attribute(
                            numPr?.Element(Word + "ilvl")
                                ?? new XElement("empty"),
                            Word + "val"));
                },
                StringComparer.Ordinal);
    }

    private static StyleNumbering ResolveStyleNumbering(
        string styleId,
        IReadOnlyDictionary<string, StyleNumbering> styles,
        ISet<string> visited)
    {
        if (string.IsNullOrEmpty(styleId)
            || !visited.Add(styleId)
            || !styles.TryGetValue(styleId, out StyleNumbering? style))
        {
            return StyleNumbering.Empty;
        }

        if (!string.IsNullOrEmpty(style.NumId))
        {
            return style;
        }

        return ResolveStyleNumbering(style.BasedOn, styles, visited);
    }

    private static IEnumerable<string> MediaSignatures(
        MainDocumentPart main)
    {
        foreach (OpenXmlPart part in EnumerateParts(main)
                     .Where(
                         part => part.ContentType.StartsWith(
                             "image/",
                             StringComparison.OrdinalIgnoreCase))
                     .OrderBy(
                         part => part.Uri.ToString(),
                         StringComparer.Ordinal))
        {
            using Stream stream = part.GetStream(
                FileMode.Open,
                FileAccess.Read);
            yield return $"{part.Uri}:{Hash(stream)}";
        }
    }

    private static IEnumerable<OpenXmlPart> EnumerateParts(
        OpenXmlPartContainer root)
    {
        var visited = new HashSet<Uri>();
        var queue = new Queue<OpenXmlPart>(
            root.Parts.Select(pair => pair.OpenXmlPart));
        while (queue.Count > 0)
        {
            OpenXmlPart part = queue.Dequeue();
            if (!visited.Add(part.Uri))
            {
                continue;
            }

            yield return part;
            foreach (IdPartPair child in part.Parts)
            {
                queue.Enqueue(child.OpenXmlPart);
            }
        }
    }

    private static string HyperlinkSignature(
        XElement hyperlink,
        MainDocumentPart main)
    {
        string relationshipId =
            Attribute(hyperlink, Relationships + "id");
        string target = main.HyperlinkRelationships
            .FirstOrDefault(
                relationship => string.Equals(
                    relationship.Id,
                    relationshipId,
                    StringComparison.Ordinal))
            ?.Uri.ToString()
            ?? string.Empty;
        return EncodeSequence(
        [
            relationshipId,
            target,
            Attribute(hyperlink, Word + "anchor"),
            Attribute(hyperlink, Word + "docLocation"),
            Normalize(string.Concat(TextNodes(hyperlink))),
        ]);
    }

    private static IEnumerable<string> BookmarkSignatures(XElement body)
    {
        foreach (XElement start in body.Descendants(Word + "bookmarkStart"))
        {
            yield return EncodeSequence(
            [
                "start",
                Attribute(start, Word + "id"),
                Attribute(start, Word + "name"),
            ]);
        }

        foreach (XElement end in body.Descendants(Word + "bookmarkEnd"))
        {
            yield return EncodeSequence(
            [
                "end",
                Attribute(end, Word + "id"),
            ]);
        }
    }

    private static IEnumerable<string> FieldSignatures(XElement body)
    {
        foreach (XElement field in body.Descendants(Word + "fldSimple"))
        {
            yield return "simple:" + Attribute(field, Word + "instr");
        }

        foreach (XElement instruction in body.Descendants(Word + "instrText"))
        {
            yield return "instruction:" + Normalize(instruction.Value);
        }

        foreach (XElement marker in body.Descendants(Word + "fldChar"))
        {
            yield return "marker:" + Attribute(
                marker,
                Word + "fldCharType");
        }
    }

    private static IEnumerable<string> RevisionSignatures(XElement body)
    {
        string[] names =
        [
            "ins",
            "del",
            "moveFrom",
            "moveTo",
            "pPrChange",
            "rPrChange",
            "tblPrChange",
            "trPrChange",
            "tcPrChange",
        ];
        var selected = new HashSet<XName>(
            names.Select(name => Word + name));
        return body.Descendants()
            .Where(element => selected.Contains(element.Name))
            .Select(RevisionSignature);
    }

    private static string EquationSignature(XElement equation)
    {
        XElement semantic = new(equation);
        semantic.Descendants()
            .Where(
                element => element.Name == Word + "rPr"
                    || element.Name == Math + "rPr"
                    || element.Name == Math + "ctrlPr")
            .Remove();
        return StructuralXml(semantic);
    }

    private static string RevisionSignature(XElement revision)
    {
        if (revision.Name.LocalName.EndsWith(
                "PrChange",
                StringComparison.Ordinal))
        {
            return StructuralXml(revision);
        }

        XElement semantic = new(revision);
        HashSet<XName> formattingContainers =
        [
            Word + "rPr",
            Word + "pPr",
            Word + "tblPr",
            Word + "trPr",
            Word + "tcPr",
            Word + "sectPr",
        ];
        semantic.Descendants()
            .Where(element => formattingContainers.Contains(element.Name))
            .Remove();
        return StructuralXml(semantic);
    }

    private static string StructuralXml(XElement element)
    {
        string[] attributes = element.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration)
            .OrderBy(
                attribute => attribute.Name.NamespaceName,
                StringComparer.Ordinal)
            .ThenBy(
                attribute => attribute.Name.LocalName,
                StringComparer.Ordinal)
            .Select(
                attribute => EncodeSequence(
                [
                    attribute.Name.NamespaceName,
                    attribute.Name.LocalName,
                    attribute.Value.Normalize(NormalizationForm.FormC),
                ]))
            .ToArray();
        bool contentTextElement = element.Name == Word + "t"
            || element.Name == Word + "delText"
            || element.Name == Word + "instrText"
            || element.Name == Math + "t";
        string[] children = element.Nodes()
            .Select(
                node => node switch
                {
                    XElement child => "element:" + StructuralXml(child),
                    XText text when contentTextElement
                        || !string.IsNullOrWhiteSpace(text.Value) =>
                        "text:" + text.Value.Normalize(
                            NormalizationForm.FormC),
                    _ => null,
                })
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
        return EncodeSequence(
        [
            element.Name.NamespaceName,
            element.Name.LocalName,
            EncodeSequence(attributes),
            EncodeSequence(children),
        ]);
    }

    private static XDocument Load(OpenXmlPart part)
    {
        using Stream stream = part.GetStream(FileMode.Open, FileAccess.Read);
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static string CanonicalXml(XElement element) =>
        element.ToString(SaveOptions.DisableFormatting);

    private static string Attribute(XElement element, XName name) =>
        element.Attribute(name)?.Value ?? string.Empty;

    private static string Normalize(string value) =>
        Whitespace().Replace(
            value.Normalize(NormalizationForm.FormC),
            " ").Trim();

    private static SnapshotItem Item(
        string checkId,
        IReadOnlyCollection<string> values)
    {
        string encoded = EncodeSequence(values);
        return new SnapshotItem(checkId, values.Count, Hash(encoded));
    }

    private static string EncodeSequence(IEnumerable<string> values)
    {
        var builder = new StringBuilder();
        foreach (string value in values)
        {
            builder.Append(value.Length);
            builder.Append(':');
            builder.Append(value);
            builder.Append(';');
        }

        return builder.ToString();
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();

    private static string Hash(Stream stream) =>
        Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

    private static string Id(string prefix, string value) =>
        $"{prefix}-{Hash(value)[..20]}";

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    private sealed record SnapshotItem(
        string CheckId,
        int Count,
        string Sha256);

    private sealed record StyleNumbering(
        string BasedOn,
        string NumId,
        string Level)
    {
        public static StyleNumbering Empty { get; } =
            new(string.Empty, string.Empty, string.Empty);
    }

    private sealed record ContentSnapshot(IEnumerable<SnapshotItem> Values)
    {
        public ValueList<SnapshotItem> Items { get; } = new(
            Values.OrderBy(item => item.CheckId, StringComparer.Ordinal));
    }
}
