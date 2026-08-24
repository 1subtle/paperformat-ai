using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PaperFormat.Fixtures;

internal static class FixtureGenerator
{
    private const string CorePropertiesDirectory =
        "package/services/metadata/core-properties/";
    private const string CanonicalCorePropertiesPath =
        CorePropertiesDirectory + "core.psmdcp";
    private const string CorePropertiesRelationshipType =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";

    private static readonly DateTimeOffset ZipTimestamp =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly FixtureDefinition[] Definitions =
    [
        new(
            "valid-ieee-like.docx",
            "A Deterministic IEEE-Like Manuscript Fixture",
            "valid-ieee-like",
            HasWrongFormatting: false),
        new(
            "wrong-format.docx",
            "A Manuscript with Intentional Formatting Deviations",
            "wrong-format",
            HasWrongFormatting: true),
        new(
            "integrity-rich.docx",
            "A Content-Integrity Rich Manuscript Fixture",
            "integrity-rich",
            HasWrongFormatting: false),
        new(
            "single-column-ieee-like.docx",
            "A Single-Column Manuscript for IEEE Layout Conversion",
            "single-column-ieee-like",
            HasWrongFormatting: false,
            IsSingleColumn: true,
            IncludeNoteReferences: false,
            ExtraBodyParagraphCount: 10),
        new(
            "wide-layout-risk.docx",
            "A Single-Column Manuscript with Wide Layout Risks",
            "wide-layout-risk",
            HasWrongFormatting: false,
            IsSingleColumn: true,
            IncludeNoteReferences: false,
            HasWideLayoutObjects: true),
    ];

    public static IReadOnlyList<string> Generate(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var generatedFiles = new List<string>(Definitions.Length);
        foreach (var definition in Definitions)
        {
            var outputPath = Path.Combine(outputDirectory, definition.FileName);
            GenerateOne(outputPath, definition);
            generatedFiles.Add(outputPath);
        }

        return generatedFiles;
    }

    private static void GenerateOne(string outputPath, FixtureDefinition definition)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(outputPath)!,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.building");

        try
        {
            DocumentFixtureBuilder.Build(temporaryPath, definition);
            RewriteAsDeterministicArchive(temporaryPath, outputPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void RewriteAsDeterministicArchive(string sourcePath, string outputPath)
    {
        List<ArchiveEntry> entries;
        using (var sourceStream = File.OpenRead(sourcePath))
        using (var sourceArchive = new ZipArchive(
                   sourceStream,
                   ZipArchiveMode.Read,
                   leaveOpen: false))
        {
            entries = sourceArchive.Entries
                .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
                .Select(entry =>
                {
                    using var entryStream = entry.Open();
                    using var content = new MemoryStream();
                    entryStream.CopyTo(content);
                    return new ArchiveEntry(entry.FullName, content.ToArray());
                })
                .ToList();
        }

        CanonicalizeCoreProperties(entries);

        using var outputStream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        using var outputArchive = new ZipArchive(
            outputStream,
            ZipArchiveMode.Create,
            leaveOpen: false);

        foreach (var entry in entries)
        {
            var outputEntry = outputArchive.CreateEntry(
                entry.FullName,
                CompressionLevel.Optimal);
            outputEntry.LastWriteTime = ZipTimestamp;

            using var outputEntryStream = outputEntry.Open();
            outputEntryStream.Write(entry.Content);
        }
    }

    private static void CanonicalizeCoreProperties(List<ArchiveEntry> entries)
    {
        ArchiveEntry? coreProperties = entries.SingleOrDefault(
            entry =>
                entry.FullName.StartsWith(
                    CorePropertiesDirectory,
                    StringComparison.Ordinal)
                && entry.FullName.EndsWith(
                    ".psmdcp",
                    StringComparison.Ordinal));
        if (coreProperties is null)
        {
            return;
        }

        int coreIndex = entries.IndexOf(coreProperties);
        entries[coreIndex] = coreProperties with
        {
            FullName = CanonicalCorePropertiesPath,
        };

        int relationshipsIndex = entries.FindIndex(
            entry => string.Equals(
                entry.FullName,
                "_rels/.rels",
                StringComparison.Ordinal));
        if (relationshipsIndex < 0)
        {
            return;
        }

        ArchiveEntry relationships = entries[relationshipsIndex];
        entries[relationshipsIndex] = relationships with
        {
            Content = CanonicalizeRootRelationships(relationships.Content),
        };
    }

    private static byte[] CanonicalizeRootRelationships(byte[] content)
    {
        using var input = new MemoryStream(content, writable: false);
        XDocument document = XDocument.Load(
            input,
            LoadOptions.PreserveWhitespace);
        XNamespace relationshipsNamespace =
            "http://schemas.openxmlformats.org/package/2006/relationships";
        XElement? relationship = document.Root?
            .Elements(relationshipsNamespace + "Relationship")
            .SingleOrDefault(
                element => string.Equals(
                    (string?)element.Attribute("Type"),
                    CorePropertiesRelationshipType,
                    StringComparison.Ordinal));
        if (relationship is null)
        {
            return content;
        }

        relationship.SetAttributeValue(
            "Target",
            "/" + CanonicalCorePropertiesPath);
        relationship.SetAttributeValue("Id", "rIdCoreProperties");

        using var output = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(
                   output,
                   new XmlWriterSettings
                   {
                       Encoding = new UTF8Encoding(
                           encoderShouldEmitUTF8Identifier: true),
                       Indent = false,
                       OmitXmlDeclaration = false,
                   }))
        {
            document.Save(writer);
        }

        return output.ToArray();
    }

    private sealed record ArchiveEntry(string FullName, byte[] Content);
}

internal sealed record FixtureDefinition(
    string FileName,
    string Title,
    string Tag,
    bool HasWrongFormatting,
    bool IsSingleColumn = false,
    bool IncludeNoteReferences = true,
    int ExtraBodyParagraphCount = 0,
    bool HasWideLayoutObjects = false);
