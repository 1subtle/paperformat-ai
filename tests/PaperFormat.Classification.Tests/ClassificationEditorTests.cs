using PaperFormat.Domain;
using PaperFormat.OpenXml;

namespace PaperFormat.Classification.Tests;

public sealed class ClassificationEditorTests
{
    [Fact]
    public void OverrideCreatesANewUserConfirmedRevision()
    {
        ClassificationSet original = Classify();
        DocumentElement source = original.Elements.First(
            element => element.Kind == ManuscriptElementKind.Unclassified);

        ClassificationSet edited = ClassificationEditor.Apply(
            original,
            [
                new ClassificationOverride(
                    source.ElementId,
                    ManuscriptElementKind.Body),
            ]);

        DocumentElement changed = edited.Elements.First(
            element => element.ElementId == source.ElementId);
        Assert.Equal(1, original.Revision);
        Assert.Equal(2, edited.Revision);
        Assert.Equal(ManuscriptElementKind.Body, changed.Kind);
        Assert.Equal(ClassificationStatus.UserConfirmed, changed.Status);
        Assert.Equal(1m, changed.Confidence);
        Assert.Equal(
            ClassificationEvidenceKind.UserOverride,
            Assert.Single(changed.Reasons).EvidenceKind);
        Assert.Equal(
            ManuscriptElementKind.Unclassified,
            original.Elements.First(
                element => element.ElementId == source.ElementId).Kind);
    }

    [Fact]
    public void OverrideRejectsUnknownAndDuplicateElementIds()
    {
        ClassificationSet source = Classify();
        string elementId = source.Elements[0].ElementId;

        Assert.Throws<ArgumentException>(
            () => ClassificationEditor.Apply(
                source,
                [
                    new ClassificationOverride(
                        "missing",
                        ManuscriptElementKind.Body),
                ]));
        Assert.Throws<ArgumentException>(
            () => ClassificationEditor.Apply(
                source,
                [
                    new ClassificationOverride(
                        elementId,
                        ManuscriptElementKind.Title),
                    new ClassificationOverride(
                        elementId,
                        ManuscriptElementKind.Author),
                ]));
    }

    private static ClassificationSet Classify()
    {
        DocumentParseResult parsed = WordDocumentParser.Parse(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "valid-ieee-like.docx"));
        DocumentModel document = Assert.IsType<DocumentModel>(parsed.Document);
        return new DeterministicDocumentClassifier().Classify(document);
    }
}
