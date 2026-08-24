using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PaperFormat.Domain;
using PaperFormat.OpenXml;

namespace PaperFormat.Classification.Tests;

public sealed class DeterministicDocumentClassifierTests
{
    [Fact]
    public void ValidFixtureMatchesGoldenElementCounts()
    {
        ClassificationSet result = Classify("valid-ieee-like.docx");

        Assert.Equal(20, result.Elements.Count);
        AssertKindCount(result, ManuscriptElementKind.Title, 1);
        AssertKindCount(result, ManuscriptElementKind.Author, 1);
        AssertKindCount(result, ManuscriptElementKind.Affiliation, 1);
        AssertKindCount(result, ManuscriptElementKind.Abstract, 1);
        AssertKindCount(result, ManuscriptElementKind.Keywords, 1);
        AssertKindCount(result, ManuscriptElementKind.Heading1, 1);
        AssertKindCount(result, ManuscriptElementKind.Heading2, 1);
        AssertKindCount(result, ManuscriptElementKind.Heading3, 1);
        AssertKindCount(result, ManuscriptElementKind.Body, 3);
        AssertKindCount(result, ManuscriptElementKind.FigureCaption, 1);
        AssertKindCount(result, ManuscriptElementKind.TableCaption, 1);
        AssertKindCount(result, ManuscriptElementKind.TableText, 6);
        AssertKindCount(result, ManuscriptElementKind.Unclassified, 1);
        ManuscriptElementKind[] expectedMainSequence =
        [
            ManuscriptElementKind.Title,
            ManuscriptElementKind.Author,
            ManuscriptElementKind.Affiliation,
            ManuscriptElementKind.Abstract,
            ManuscriptElementKind.Keywords,
            ManuscriptElementKind.Heading1,
            ManuscriptElementKind.Body,
            ManuscriptElementKind.Heading2,
            ManuscriptElementKind.Body,
            ManuscriptElementKind.Heading3,
            ManuscriptElementKind.Body,
            ManuscriptElementKind.Unclassified,
            ManuscriptElementKind.FigureCaption,
            ManuscriptElementKind.TableCaption,
        ];
        Assert.Equal(
            expectedMainSequence,
            result.Elements
                .Where(element => element.Location.TableIndex is null)
                .OrderBy(element => element.Location.ParagraphIndex)
                .Select(element => element.Kind));
        Assert.DoesNotContain(
            result.Elements,
            element => element.Status == ClassificationStatus.NeedsConfirmation);
    }

    [Fact]
    public void FormattingDeviationsDoNotChangeSemanticClassifications()
    {
        ClassificationSet valid = Classify("valid-ieee-like.docx");
        ClassificationSet wrong = Classify("wrong-format.docx");

        Assert.Equal(
            valid.Elements.Select(element => (element.ElementId, element.Kind)),
            wrong.Elements.Select(element => (element.ElementId, element.Kind)));
    }

    [Fact]
    public void ConfirmedElementsContainIndependentEvidenceCategories()
    {
        ClassificationSet result = Classify("valid-ieee-like.docx");
        DocumentElement title = Assert.Single(
            result.Elements,
            element => element.Kind == ManuscriptElementKind.Title);
        DocumentElement body = result.Elements.First(
            element => element.Kind == ManuscriptElementKind.Body);

        Assert.Equal(ClassificationStatus.Confirmed, title.Status);
        Assert.True(
            title.Reasons.Select(reason => reason.EvidenceKind).Distinct().Count()
            >= 3);
        Assert.Equal(ClassificationStatus.Confirmed, body.Status);
        Assert.True(
            body.Reasons.Select(reason => reason.EvidenceKind).Distinct().Count()
            >= 2);
    }

    [Fact]
    public void WeakEvidenceRemainsPendingInsteadOfBeingForced()
    {
        var location = new StructuralLocation(
            DocumentPartKind.MainDocument,
            sectionIndex: 0,
            paragraphIndex: 0);
        var paragraph = new ParagraphModel(
            location,
            blockIndex: 0,
            styleId: null,
            new DocumentText("Possible heading"),
            ParagraphFormatting.Empty,
            ParagraphFormatting.Empty,
            [
                new RunModel(
                    new StructuralLocation(
                        DocumentPartKind.MainDocument,
                        sectionIndex: 0,
                        paragraphIndex: 0,
                        runIndex: 0),
                    styleId: null,
                    new DocumentText("Possible heading"),
                    CharacterFormatting.Empty,
                    CharacterFormatting.Empty),
            ]);
        var document = new DocumentModel(
            WordPackageKind.Document,
            DocumentDefaults.Empty,
            Array.Empty<StyleDefinition>(),
            [
                new SectionModel(
                    new StructuralLocation(
                        DocumentPartKind.MainDocument,
                        sectionIndex: 0),
                    PageSettings.Empty,
                    [paragraph],
                    Array.Empty<TableModel>()),
            ]);

        DocumentElement element = Assert.Single(
            new DeterministicDocumentClassifier()
                .Classify(document)
                .Elements);

        Assert.Equal(ManuscriptElementKind.Title, element.Kind);
        Assert.Equal(ClassificationStatus.NeedsConfirmation, element.Status);
        Assert.Equal(0.50m, element.Confidence);
    }

    [Fact]
    public void ModerateSemanticStyleEvidenceIsConfirmedForFormatting()
    {
        DocumentModel source = Document(
            "Preface note",
            "Additional note",
            "Another note",
            "Alice Example");
        ParagraphModel original = source.Sections[0].Paragraphs[3];
        var author = new ParagraphModel(
            original.Location,
            original.BlockIndex,
            "Author",
            original.Text,
            ParagraphFormatting.Empty,
            new ParagraphFormatting(ParagraphAlignment.Center),
            original.Runs);
        var style = new StyleDefinition(
            "Author",
            "Author",
            StyleKind.Paragraph,
            basedOnStyleId: null,
            linkedStyleId: null,
            isDefault: false,
            isCustom: true,
            ParagraphFormatting.Empty,
            CharacterFormatting.Empty,
            TableFormatting.Empty);
        var document = new DocumentModel(
            source.PackageKind,
            source.Defaults,
            [style],
            [
                new SectionModel(
                    source.Sections[0].Location,
                    source.Sections[0].PageSettings,
                    source.Sections[0].Paragraphs.Take(3).Append(author),
                    Array.Empty<TableModel>()),
            ]);

        DocumentElement element = Assert.Single(
            new DeterministicDocumentClassifier()
                .Classify(document)
                .Elements,
            item => item.Location.ParagraphIndex == 3);

        Assert.Equal(ManuscriptElementKind.Author, element.Kind);
        Assert.Equal(ClassificationStatus.Confirmed, element.Status);
        Assert.Equal(0.60m, element.Confidence);
    }

    [Fact]
    public void PatternAndContextClassifyReferencesWithoutSemanticStyles()
    {
        DocumentModel document = Document(
            "A Paper Title",
            "REFERENCES",
            "[1] A. Example, A deterministic reference.");

        ClassificationSet result =
            new DeterministicDocumentClassifier().Classify(document);

        Assert.Contains(
            result.Elements,
            element => element.Kind
                == ManuscriptElementKind.ReferencesHeading
                && element.Status == ClassificationStatus.Confirmed);
        Assert.Contains(
            result.Elements,
            element => element.Kind == ManuscriptElementKind.ReferenceEntry
                && element.Status == ClassificationStatus.Confirmed);
    }

    [Fact]
    public void ReferenceStyleSuppressesAuthorInitialHeadingFalsePositive()
    {
        DocumentModel source = Document(
            "REFERENCES",
            "A. Example, B. Researcher, and C. Author, A long journal article title.");
        ParagraphModel original = source.Sections[0].Paragraphs[1];
        var reference = new ParagraphModel(
            original.Location,
            original.BlockIndex,
            "References",
            original.Text,
            ParagraphFormatting.Empty,
            new ParagraphFormatting(ParagraphAlignment.Justified),
            original.Runs);
        var style = new StyleDefinition(
            "References",
            "References",
            StyleKind.Paragraph,
            basedOnStyleId: null,
            linkedStyleId: null,
            isDefault: false,
            isCustom: true,
            ParagraphFormatting.Empty,
            CharacterFormatting.Empty,
            TableFormatting.Empty);
        var document = new DocumentModel(
            source.PackageKind,
            source.Defaults,
            [style],
            [
                new SectionModel(
                    source.Sections[0].Location,
                    source.Sections[0].PageSettings,
                    [source.Sections[0].Paragraphs[0], reference],
                    Array.Empty<TableModel>()),
            ]);

        DocumentElement element = Assert.Single(
            new DeterministicDocumentClassifier()
                .Classify(document)
                .Elements,
            item => item.Location.ParagraphIndex == 1);

        Assert.Equal(ManuscriptElementKind.ReferenceEntry, element.Kind);
        Assert.Equal(ClassificationStatus.Confirmed, element.Status);
    }

    [Fact]
    public void PatternRecognitionWorksWithoutWordStyles()
    {
        DocumentModel document = Document(
            "A Paper Title",
            "Abstract—A concise summary.",
            "Index Terms—DOCX, checking.",
            "I. INTRODUCTION",
            "Fig. 1. Result.",
            "TABLE I. RESULT");

        ClassificationSet result =
            new DeterministicDocumentClassifier().Classify(document);

        ManuscriptElementKind[] expected =
        [
            ManuscriptElementKind.Title,
            ManuscriptElementKind.Abstract,
            ManuscriptElementKind.Keywords,
            ManuscriptElementKind.Heading1,
            ManuscriptElementKind.FigureCaption,
            ManuscriptElementKind.TableCaption,
        ];
        Assert.Equal(expected, result.Elements.Select(element => element.Kind));
    }

    [Fact]
    public void ClassificationJsonIsDeterministicAndContentSafe()
    {
        ClassificationSet result = Classify("valid-ieee-like.docx");

        string first = ClassificationJson.Serialize(result);
        ClassificationSet roundTrip = ClassificationJson.Deserialize(first);
        string second = ClassificationJson.Serialize(
            roundTrip);

        Assert.Equal(first, second);
        Assert.DoesNotContain(
            "synthetic manuscript exercises",
            first,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"textLength\"", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassificationMatchesApprovedCanonicalSnapshot()
    {
        ClassificationSet result = Classify("valid-ieee-like.docx");
        string json = ClassificationJson.Serialize(result);
        using JsonDocument snapshot = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Snapshots",
                    "valid-ieee-like-v1.json")));
        JsonElement root = snapshot.RootElement;
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();

        Assert.Equal(
            result.Elements.Count,
            root.GetProperty("elementCount").GetInt32());
        Assert.Equal(
            result.Elements.Count(
                element => element.Status == ClassificationStatus.Confirmed),
            root.GetProperty("confirmedCount").GetInt32());
        Assert.Equal(
            result.Elements.Count(
                element => element.Status
                    == ClassificationStatus.NeedsConfirmation),
            root.GetProperty("pendingCount").GetInt32());
        Assert.Equal(
            result.Elements.Count(
                element => element.Status == ClassificationStatus.Unclassified),
            root.GetProperty("unclassifiedCount").GetInt32());
        string? expectedDigest =
            root.GetProperty("canonicalJsonSha256").GetString();
        Assert.True(
            string.Equals(
                expectedDigest,
                digest,
                StringComparison.Ordinal),
            $"Expected digest: {expectedDigest}; actual digest: {digest}");
    }

    private static ClassificationSet Classify(string fileName)
    {
        DocumentParseResult parsed = WordDocumentParser.Parse(Fixture(fileName));
        DocumentModel document = Assert.IsType<DocumentModel>(parsed.Document);
        return new DeterministicDocumentClassifier().Classify(document);
    }

    private static void AssertKindCount(
        ClassificationSet result,
        ManuscriptElementKind kind,
        int expected) =>
        Assert.Equal(
            expected,
            result.Elements.Count(element => element.Kind == kind));

    private static DocumentModel Document(params string[] paragraphs)
    {
        ParagraphModel[] models = paragraphs
            .Select(
                (text, index) =>
                {
                    var location = new StructuralLocation(
                        DocumentPartKind.MainDocument,
                        sectionIndex: 0,
                        paragraphIndex: index);
                    return new ParagraphModel(
                        location,
                        blockIndex: index,
                        styleId: null,
                        new DocumentText(text),
                        ParagraphFormatting.Empty,
                        ParagraphFormatting.Empty,
                        [
                            new RunModel(
                                new StructuralLocation(
                                    DocumentPartKind.MainDocument,
                                    sectionIndex: 0,
                                    paragraphIndex: index,
                                    runIndex: 0),
                                styleId: null,
                                new DocumentText(text),
                                CharacterFormatting.Empty,
                                CharacterFormatting.Empty),
                        ]);
                })
            .ToArray();
        return new DocumentModel(
            WordPackageKind.Document,
            DocumentDefaults.Empty,
            Array.Empty<StyleDefinition>(),
            [
                new SectionModel(
                    new StructuralLocation(
                        DocumentPartKind.MainDocument,
                        sectionIndex: 0),
                    PageSettings.Empty,
                    models,
                    Array.Empty<TableModel>()),
            ]);
    }

    private static string Fixture(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
