using System.IO.Compression;
using System.Text;
using PaperFormat.OpenXml;

namespace PaperFormat.OpenXml.Tests;

public sealed class PreflightPackageInspectorTests
{
    private const string ContentTypesXml =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="xml" ContentType="application/xml" />
        </Types>
        """;

    private const string DocumentXml =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body><w:p /></w:body>
        </w:document>
        """;

    [Theory]
    [InlineData("paper.docx")]
    [InlineData("template.dotx")]
    [InlineData("PAPER.DOCX")]
    public void InspectAcceptsValidDocxAndDotxStreams(string fileName)
    {
        using MemoryStream package = CreatePackage();

        PackagePreflightResult result = DocxPackagePreflight.Inspect(package, fileName);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
        PackagePreflightSummary summary =
            Assert.IsType<PackagePreflightSummary>(result.Summary);
        Assert.Equal(2, summary.EntryCount);
        Assert.Equal(0, package.Position);
    }

    [Fact]
    public void InspectAcceptsTheGeneratedSyntheticFixture()
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "valid-ieee-like.docx");

        PackagePreflightResult result = DocxPackagePreflight.Inspect(fixturePath);

        Assert.True(result.IsValid);
        PackagePreflightSummary summary =
            Assert.IsType<PackagePreflightSummary>(result.Summary);
        Assert.True(summary.EntryCount > 2);
    }

    [Fact]
    public void InspectPathOverloadReadsAValidPackage()
    {
        using MemoryStream package = CreatePackage();
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");

        try
        {
            File.WriteAllBytes(path, package.ToArray());

            PackagePreflightResult result = DocxPackagePreflight.Inspect(path);

            Assert.True(result.IsValid);
            Assert.NotNull(result.Summary);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InspectRejectsAValidZipWithAnUnsupportedExtension()
    {
        using MemoryStream package = CreatePackage();

        PackagePreflightResult result = DocxPackagePreflight.Inspect(package, "paper.pdf");

        PackagePreflightDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsValid);
        Assert.Equal(
            PackagePreflightDiagnosticCode.UnsupportedExtension,
            diagnostic.Code);
    }

    [Fact]
    public void InspectRejectsContentWithoutAZipSignatureWithoutLeakingIt()
    {
        const string manuscriptText = "CONFIDENTIAL MANUSCRIPT BODY";
        using MemoryStream package = new(Encoding.UTF8.GetBytes(manuscriptText));

        PackagePreflightResult result = DocxPackagePreflight.Inspect(package, "paper.docx");

        PackagePreflightDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            PackagePreflightDiagnosticCode.InvalidZipSignature,
            diagnostic.Code);
        Assert.False(
            diagnostic.Message.Contains(manuscriptText, StringComparison.Ordinal));
    }

    [Fact]
    public void InspectReturnsAStructuredDiagnosticForACorruptZip()
    {
        byte[] corruptPackage = [0x50, 0x4B, 0x03, 0x04, 0x01, 0x02, 0x03, 0x04];
        using MemoryStream package = new(corruptPackage);

        PackagePreflightResult result = DocxPackagePreflight.Inspect(package, "paper.docx");

        PackagePreflightDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsValid);
        Assert.Equal(PackagePreflightDiagnosticCode.CorruptZip, diagnostic.Code);
    }

    [Theory]
    [InlineData("../outside.xml")]
    [InlineData("word/../../outside.xml")]
    [InlineData("/absolute.xml")]
    [InlineData(@"C:\absolute.xml")]
    public void InspectRejectsTraversalAndAbsoluteEntryPaths(string unsafePath)
    {
        using MemoryStream package = CreatePackage(
            archive => WriteEntry(archive, unsafePath, "metadata"));

        PackagePreflightResult result = DocxPackagePreflight.Inspect(
            package,
            "paper.docx");

        PackagePreflightDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Code == PackagePreflightDiagnosticCode.UnsafeEntryPath);
        Assert.False(result.IsValid);
        Assert.NotNull(diagnostic.EntryPath);
    }

    [Fact]
    public void InspectRejectsBombLikeCompressionRatio()
    {
        string highlyCompressibleBody = new('A', 1024 * 1024);
        using MemoryStream package = CreatePackage(
            documentXml: highlyCompressibleBody);

        PackagePreflightResult result = DocxPackagePreflight.Inspect(
            package,
            "paper.docx");

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Diagnostics,
            item => item.Code
                == PackagePreflightDiagnosticCode.CompressionRatioExceeded);
    }

    [Fact]
    public void InspectRejectsAnEntryAboveTheExpandedSizeLimit()
    {
        using MemoryStream package = CreatePackage(documentXml: new string('A', 2048));
        PackagePreflightOptions options = new()
        {
            MaxEntryExpandedBytes = 1024,
        };

        PackagePreflightResult result = DocxPackagePreflight.Inspect(
            package,
            "paper.docx",
            options);

        Assert.Contains(
            result.Diagnostics,
            item => item.Code == PackagePreflightDiagnosticCode.EntryTooLarge);
    }

    [Fact]
    public void InspectRejectsTotalExpandedSizeAboveTheLimit()
    {
        using MemoryStream package = CreatePackage();
        PackagePreflightOptions options = new()
        {
            MaxTotalExpandedBytes = 100,
        };

        PackagePreflightResult result = DocxPackagePreflight.Inspect(
            package,
            "paper.docx",
            options);

        Assert.Contains(
            result.Diagnostics,
            item => item.Code
                == PackagePreflightDiagnosticCode.ExpandedSizeTooLarge);
    }

    [Fact]
    public void InspectRejectsTooManyEntries()
    {
        using MemoryStream package = CreatePackage(
            archive => WriteEntry(archive, "custom.xml", "metadata"));
        PackagePreflightOptions options = new()
        {
            MaxEntryCount = 2,
        };

        PackagePreflightResult result = DocxPackagePreflight.Inspect(
            package,
            "paper.docx",
            options);

        PackagePreflightDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            PackagePreflightDiagnosticCode.TooManyEntries,
            diagnostic.Code);
    }

    [Fact]
    public void InspectRejectsAPackageAboveTheCompressedSizeLimit()
    {
        using MemoryStream package = CreatePackage();
        PackagePreflightOptions options = new()
        {
            MaxPackageBytes = 8,
        };

        PackagePreflightResult result = DocxPackagePreflight.Inspect(
            package,
            "paper.docx",
            options);

        PackagePreflightDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            PackagePreflightDiagnosticCode.PackageTooLarge,
            diagnostic.Code);
    }

    [Fact]
    public void InspectRejectsMissingContentTypesPart()
    {
        using MemoryStream package = CreatePackage(includeContentTypes: false);

        PackagePreflightResult result = DocxPackagePreflight.Inspect(
            package,
            "paper.docx");

        Assert.Contains(
            result.Diagnostics,
            item => item.Code
                == PackagePreflightDiagnosticCode.MissingContentTypes);
    }

    [Fact]
    public void InspectRejectsMissingMainDocumentPart()
    {
        using MemoryStream package = CreatePackage(includeMainDocument: false);

        PackagePreflightResult result = DocxPackagePreflight.Inspect(
            package,
            "paper.docx");

        Assert.Contains(
            result.Diagnostics,
            item => item.Code
                == PackagePreflightDiagnosticCode.MissingMainDocument);
    }

    private static MemoryStream CreatePackage(
        Action<ZipArchive>? addEntries = null,
        bool includeContentTypes = true,
        bool includeMainDocument = true,
        string? documentXml = null)
    {
        MemoryStream package = new();
        using (ZipArchive archive = new(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeContentTypes)
            {
                WriteEntry(archive, "[Content_Types].xml", ContentTypesXml);
            }

            if (includeMainDocument)
            {
                WriteEntry(
                    archive,
                    "word/document.xml",
                    documentXml ?? DocumentXml);
            }

            addEntries?.Invoke(archive);
        }

        package.Position = 0;
        return package;
    }

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream entryStream = entry.Open();
        using StreamWriter writer = new(
            entryStream,
            Encoding.UTF8,
            bufferSize: 1024,
            leaveOpen: false);
        writer.Write(content);
    }
}
