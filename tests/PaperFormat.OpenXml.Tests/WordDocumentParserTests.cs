using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using PaperFormat.Domain;

namespace PaperFormat.OpenXml.Tests;

public sealed class WordDocumentParserTests
{
    [Theory]
    [InlineData("valid-ieee-like.docx")]
    [InlineData("wrong-format.docx")]
    [InlineData("integrity-rich.docx")]
    public void GeneratedFixturesPassOpenXmlValidation(string fileName)
    {
        using WordprocessingDocument document =
            WordprocessingDocument.Open(Fixture(fileName), false);
        OpenXmlValidator validator = new();
        List<ValidationErrorInfo> errors = validator.Validate(document).ToList();

        Assert.True(
            errors.Count == 0,
            string.Join(
                Environment.NewLine,
                errors.Select(
                    error =>
                        $"{error.Id}: {error.Description} at {error.Path?.XPath}")));
    }

    [Fact]
    public void ParseReadsTheGeneratedSyntheticFixture()
    {
        DocumentParseResult result =
            WordDocumentParser.Parse(Fixture("valid-ieee-like.docx"));

        Assert.True(result.IsSuccess);
        DocumentModel document = Assert.IsType<DocumentModel>(result.Document);
        Assert.True(document.Styles.Count >= 10);
        Assert.NotEmpty(document.Sections);
        Assert.Contains(
            document.Sections,
            section => section.PageSettings.Columns.Count == 2);
    }

    [Fact]
    public void ParseResolvesGeneratedPageAndBodyFormatting()
    {
        DocumentParseResult result =
            WordDocumentParser.Parse(Fixture("valid-ieee-like.docx"));

        Assert.True(result.IsSuccess);
        DocumentModel document = Assert.IsType<DocumentModel>(result.Document);
        SectionModel section = Assert.Single(document.Sections);
        Assert.Equal(new Twip(12_240), section.PageSettings.Width);
        Assert.Equal(new Twip(15_840), section.PageSettings.Height);
        Assert.Equal(PageOrientation.Portrait, section.PageSettings.Orientation);
        Assert.Equal(2, section.PageSettings.Columns.Count);
        Assert.Equal(new Twip(360), section.PageSettings.Columns.Spacing);
        Assert.Single(section.Tables);

        ParagraphModel body = Assert.Single(
            section.Paragraphs,
            paragraph =>
                string.Equals(
                    paragraph.StyleId,
                    "BodyText",
                    StringComparison.Ordinal)
                && paragraph.Text.Value.StartsWith(
                    "Each package uses",
                    StringComparison.Ordinal));
        Assert.Equal(
            ParagraphAlignment.Justified,
            body.EffectiveFormatting.Alignment);
        RunModel run = Assert.Single(body.Runs);
        Assert.Equal(
            "Times New Roman",
            run.EffectiveFormatting.Fonts?.Ascii);
        Assert.Equal(Twip.FromPoints(10m), run.EffectiveFormatting.FontSize);
    }

    [Fact]
    public void ParseDirectFormattingOverridesInheritedStyle()
    {
        DocumentParseResult result =
            WordDocumentParser.Parse(Fixture("wrong-format.docx"));

        Assert.True(result.IsSuccess);
        DocumentModel document = Assert.IsType<DocumentModel>(result.Document);
        ParagraphModel paragraph = Assert.Single(
            Assert.Single(document.Sections).Paragraphs,
            item => item.Text.Value.StartsWith(
                "Each package uses",
                StringComparison.Ordinal));
        RunModel run = Assert.Single(paragraph.Runs);

        Assert.Equal(
            ParagraphAlignment.Right,
            paragraph.DirectFormatting.Alignment);
        Assert.Equal(
            ParagraphAlignment.Right,
            paragraph.EffectiveFormatting.Alignment);
        Assert.Equal(
            "Courier New",
            run.DirectFormatting.Fonts?.Ascii);
        Assert.Equal(
            "Courier New",
            run.EffectiveFormatting.Fonts?.Ascii);
        Assert.Equal(Twip.FromPoints(14m), run.EffectiveFormatting.FontSize);
    }

    [Fact]
    public void ParseIsDeterministicForTheSamePackage()
    {
        string path = Fixture("integrity-rich.docx");

        DocumentParseResult first = WordDocumentParser.Parse(path);
        DocumentParseResult second = WordDocumentParser.Parse(path);

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Document, second.Document);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
    }

    [Fact]
    public void ParseDoesNotIndexOfficeMathRunsAsWordRuns()
    {
        DocumentParseResult result =
            WordDocumentParser.Parse(Fixture("integrity-rich.docx"));

        Assert.True(result.IsSuccess);
        DocumentModel document = Assert.IsType<DocumentModel>(result.Document);
        ParagraphModel equation = Assert.Single(
            document.Sections
                .SelectMany(section => section.Paragraphs),
            paragraph => paragraph.Text.Value.Contains(
                "E = mc²",
                StringComparison.Ordinal));

        Assert.Empty(equation.Runs);
    }

    [Fact]
    public void ParseStreamRestoresItsOriginalPosition()
    {
        byte[] package = File.ReadAllBytes(Fixture("valid-ieee-like.docx"));
        using var padded = new MemoryStream();
        padded.Write([0x01, 0x02, 0x03]);
        padded.Write(package);
        padded.Position = 3;

        DocumentParseResult result =
            WordDocumentParser.Parse(padded, "manuscript.docx");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, padded.Position);
    }

    private static string Fixture(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
