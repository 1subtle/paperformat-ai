using System.Text.Json;

namespace PaperFormat.Domain.Tests;

public sealed class DocumentModelTests
{
    [Fact]
    public void ModelTakesImmutableCollectionSnapshots()
    {
        var paragraphs = new List<ParagraphModel> { CreateParagraph() };
        var section = new SectionModel(
            new StructuralLocation(
                DocumentPartKind.MainDocument,
                sectionIndex: 0),
            PageSettings.Empty,
            paragraphs,
            Array.Empty<TableModel>());
        var sections = new List<SectionModel> { section };
        var document = new DocumentModel(
            WordPackageKind.Document,
            DocumentDefaults.Empty,
            Array.Empty<StyleDefinition>(),
            sections);

        paragraphs.Clear();
        sections.Clear();

        Assert.Single(document.Sections);
        Assert.Single(document.Sections[0].Paragraphs);
    }

    [Fact]
    public void IndependentlyBuiltModelsCompareAndSerializeTheSame()
    {
        var first = CreateDocument();
        var second = CreateDocument();

        Assert.Equal(first, second);
        Assert.Equal(
            JsonSerializer.Serialize(first),
            JsonSerializer.Serialize(second));
    }

    [Fact]
    public void StringRepresentationsDoNotExposeManuscriptText()
    {
        const string sensitiveText = "unpublished research result";
        var paragraph = CreateParagraph(sensitiveText);

        Assert.DoesNotContain(
            sensitiveText,
            paragraph.Text.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            sensitiveText,
            paragraph.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            sensitiveText.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            paragraph.Text.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultJsonSerializationDoesNotExposeManuscriptText()
    {
        const string sensitiveText = "unpublished research result";
        var paragraph = CreateParagraph(sensitiveText);

        var json = JsonSerializer.Serialize(paragraph);

        Assert.DoesNotContain(
            sensitiveText,
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            sensitiveText.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FailedParseRequiresStructuredErrorDiagnostic()
    {
        var warning = new ParseDiagnostic(
            "styles.missing",
            ParseDiagnosticSeverity.Warning,
            "The styles part is missing.");

        Assert.Throws<ArgumentException>(
            () => DocumentParseResult.Failure(new[] { warning }));

        var result = DocumentParseResult.Failure(
            new[]
            {
                new ParseDiagnostic(
                    "package.invalid",
                    ParseDiagnosticSeverity.Error,
                    "The OOXML package is invalid."),
            });

        Assert.False(result.IsSuccess);
        Assert.Null(result.Document);
        Assert.Single(result.Diagnostics);
    }

    private static DocumentModel CreateDocument()
    {
        var paragraph = CreateParagraph();
        var section = new SectionModel(
            new StructuralLocation(
                DocumentPartKind.MainDocument,
                sectionIndex: 0),
            new PageSettings(
                Twip.FromMillimeters(210m),
                Twip.FromMillimeters(297m),
                PageOrientation.Portrait,
                new Margins(
                    Top: Twip.FromCentimeters(2.54m),
                    Right: Twip.FromCentimeters(1.91m),
                    Bottom: Twip.FromCentimeters(2.54m),
                    Left: Twip.FromCentimeters(1.91m)),
                new Columns(
                    count: 2,
                    spacing: Twip.FromCentimeters(0.42m),
                    equalWidth: true)),
            new[] { paragraph },
            Array.Empty<TableModel>());

        return new DocumentModel(
            WordPackageKind.Document,
            DocumentDefaults.Empty,
            Array.Empty<StyleDefinition>(),
            new[] { section });
    }

    private static ParagraphModel CreateParagraph(
        string text = "Example body paragraph.")
    {
        var paragraphLocation = new StructuralLocation(
            DocumentPartKind.MainDocument,
            sectionIndex: 0,
            paragraphIndex: 0);
        var run = new RunModel(
            new StructuralLocation(
                DocumentPartKind.MainDocument,
                sectionIndex: 0,
                paragraphIndex: 0,
                runIndex: 0),
            styleId: null,
            new DocumentText(text),
            CharacterFormatting.Empty,
            new CharacterFormatting(
                new FontFamilies(Ascii: "Times New Roman"),
                Twip.FromPoints(10m)));

        return new ParagraphModel(
            paragraphLocation,
            blockIndex: 0,
            styleId: "Normal",
            new DocumentText(text),
            ParagraphFormatting.Empty,
            new ParagraphFormatting(
                Alignment: ParagraphAlignment.Justified,
                LineSpacing: LineSpacing.Automatic(
                    new LineMultiple(LineMultiple.UnitsPerLine))),
            new[] { run });
    }
}
