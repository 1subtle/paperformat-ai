using DocumentFormat.OpenXml.Math;
using DocumentFormat.OpenXml.Packaging;
using PaperFormat.Domain;
using PaperFormat.Integrity;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace PaperFormat.Repair.Tests;

public sealed class ContentIntegrityValidatorTests
{
    [Fact]
    public void IdenticalCopiesPassEveryIntegrityCheck()
    {
        string source = Fixture("integrity-rich.docx");
        string copy = CopyFixture();

        IntegrityReport report =
            ContentIntegrityValidator.Compare(source, copy);

        Assert.Equal(IntegrityStatus.Passed, report.Status);
        Assert.True(report.Checks.Count >= 13);
        Assert.All(
            report.Checks,
            check => Assert.Equal(IntegrityStatus.Passed, check.Status));
    }

    [Theory]
    [InlineData(TamperKind.BodyText, "normalized_body_text")]
    [InlineData(TamperKind.TableText, "tables")]
    [InlineData(TamperKind.Image, "media")]
    [InlineData(TamperKind.Equation, "equations")]
    [InlineData(TamperKind.Hyperlink, "hyperlinks")]
    [InlineData(TamperKind.Bookmark, "bookmarks")]
    [InlineData(TamperKind.Field, "fields")]
    [InlineData(TamperKind.Footnote, "footnotes")]
    [InlineData(TamperKind.Endnote, "endnotes")]
    [InlineData(TamperKind.Header, "headers")]
    [InlineData(TamperKind.Footer, "footers")]
    public void KnownContentTamperingFailsTheSpecificCheck(
        TamperKind tamper,
        string expectedCheck)
    {
        string source = Fixture("integrity-rich.docx");
        string changed = CopyFixture();
        ApplyTamper(changed, tamper);

        IntegrityReport report =
            ContentIntegrityValidator.Compare(source, changed);

        Assert.Equal(IntegrityStatus.Failed, report.Status);
        IntegrityCheck check = Assert.Single(
            report.Checks,
            item => item.CheckId == expectedCheck);
        Assert.Equal(IntegrityStatus.Failed, check.Status);
        Assert.NotEqual(check.SourceSha256, check.OutputSha256);
    }

    [Fact]
    public void EquationFormattingChangesDoNotCountAsEquationContentChanges()
    {
        string source = Fixture("integrity-rich.docx");
        string changed = CopyFixture();
        using (WordprocessingDocument package =
               WordprocessingDocument.Open(changed, true))
        {
            Run equationRun = package.MainDocumentPart!.Document!.Body!
                .Descendants<OfficeMath>()
                .First()
                .Descendants<Run>()
                .First();
            equationRun.RunProperties =
                new W.RunProperties(new W.Bold { Val = true });
            package.MainDocumentPart.Document.Save();
        }

        IntegrityReport report =
            ContentIntegrityValidator.Compare(source, changed);

        IntegrityCheck equations = Assert.Single(
            report.Checks,
            check => check.CheckId == "equations");
        Assert.Equal(IntegrityStatus.Passed, equations.Status);
    }

    [Fact]
    public void RevisionRunFormattingChangesPreserveRevisionIntegrity()
    {
        string source = CopyFixture();
        AddTrackedInsertion(source);
        string changed = CopyOf(source);
        using (WordprocessingDocument package =
               WordprocessingDocument.Open(changed, true))
        {
            W.Run inserted = package.MainDocumentPart!.Document!.Body!
                .Descendants<W.InsertedRun>()
                .Single()
                .Descendants<W.Run>()
                .Single();
            inserted.RunProperties =
                new W.RunProperties(new W.Bold { Val = true });
            package.MainDocumentPart.Document.Save();
        }

        IntegrityReport report =
            ContentIntegrityValidator.Compare(source, changed);

        IntegrityCheck revisions = Assert.Single(
            report.Checks,
            check => check.CheckId == "revisions");
        Assert.Equal(IntegrityStatus.Passed, revisions.Status);
    }

    [Fact]
    public void RevisionTextChangesStillFailRevisionIntegrity()
    {
        string source = CopyFixture();
        AddTrackedInsertion(source);
        string changed = CopyOf(source);
        using (WordprocessingDocument package =
               WordprocessingDocument.Open(changed, true))
        {
            package.MainDocumentPart!.Document!.Body!
                .Descendants<W.InsertedRun>()
                .Single()
                .Descendants<W.Text>()
                .Single()
                .Text = "Changed tracked text";
            package.MainDocumentPart.Document.Save();
        }

        IntegrityReport report =
            ContentIntegrityValidator.Compare(source, changed);

        IntegrityCheck revisions = Assert.Single(
            report.Checks,
            check => check.CheckId == "revisions");
        Assert.Equal(IntegrityStatus.Failed, revisions.Status);
    }

    [Fact]
    public void RevisionPropertyChangeSurvivesUnrelatedFormattingAndResave()
    {
        string source = CopyFixture();
        AddTrackedParagraphPropertyChange(source);
        string changed = CopyOf(source);
        using (WordprocessingDocument package =
               WordprocessingDocument.Open(changed, true))
        {
            W.Run run = package.MainDocumentPart!.Document!.Body!
                .Descendants<W.Run>()
                .First();
            run.RunProperties =
                new W.RunProperties(new W.Italic { Val = true });
            package.MainDocumentPart.Document.Save();
        }

        IntegrityReport report =
            ContentIntegrityValidator.Compare(source, changed);

        IntegrityCheck revisions = Assert.Single(
            report.Checks,
            check => check.CheckId == "revisions");
        Assert.Equal(IntegrityStatus.Passed, revisions.Status);
    }

    [Fact]
    public void TableGridRemovalFailsGeometryIntegrity()
    {
        string source = Fixture("integrity-rich.docx");
        string changed = CopyFixture();
        using (WordprocessingDocument package =
               WordprocessingDocument.Open(changed, true))
        {
            package.MainDocumentPart!.Document!.Body!
                .Descendants<W.TableGrid>()
                .First()
                .Remove();
            package.MainDocumentPart.Document.Save();
        }

        IntegrityReport report =
            ContentIntegrityValidator.Compare(source, changed);

        IntegrityCheck geometry = Assert.Single(
            report.Checks,
            check => check.CheckId == "table_geometry");
        Assert.Equal(IntegrityStatus.Failed, geometry.Status);
        Assert.Equal(IntegrityStatus.Failed, report.Status);
    }

    [Fact]
    public void EffectiveNumberingRemovalFailsStructuralIntegrity()
    {
        string source = CopyFixture();
        AddNumbering(source);
        string changed = CopyOf(source);
        using (WordprocessingDocument package =
               WordprocessingDocument.Open(changed, true))
        {
            W.Paragraph paragraph = package.MainDocumentPart!.Document!.Body!
                .Elements<W.Paragraph>()
                .First();
            paragraph.ParagraphProperties!.NumberingProperties = null;
            package.MainDocumentPart.Document.Save();
        }

        IntegrityReport report =
            ContentIntegrityValidator.Compare(source, changed);

        IntegrityCheck numbering = Assert.Single(
            report.Checks,
            check => check.CheckId == "effective_numbering");
        Assert.Equal(IntegrityStatus.Failed, numbering.Status);
        Assert.Equal(IntegrityStatus.Failed, report.Status);
    }

    [Fact]
    public void ApprovedSectionTopologyChangeDoesNotRelaxContentChecks()
    {
        string source = Fixture("integrity-rich.docx");
        string changed = CopyFixture();
        using (WordprocessingDocument package =
               WordprocessingDocument.Open(changed, true))
        {
            W.Body body = package.MainDocumentPart!.Document!.Body!;
            W.Paragraph boundary = body.Elements<W.Paragraph>().ElementAt(4);
            W.ParagraphProperties properties =
                boundary.ParagraphProperties
                ?? boundary.PrependChild(new W.ParagraphProperties());
            W.SectionProperties first = (
                W.SectionProperties)body
                .Elements<W.SectionProperties>()
                .Single()
                .CloneNode(true);
            properties.Append(first);
            package.MainDocumentPart.Document.Save();
        }

        IntegrityReport strict =
            ContentIntegrityValidator.Compare(source, changed);
        IntegrityReport approved = ContentIntegrityValidator.Compare(
            source,
            changed,
            [IntegrityCheckIds.SectionTopology]);

        Assert.Equal(IntegrityStatus.Failed, strict.Status);
        Assert.Equal(IntegrityStatus.Passed, approved.Status);
        IntegrityCheck topology = Assert.Single(
            approved.Checks,
            item => item.CheckId
                == IntegrityCheckIds.SectionTopology);
        Assert.Contains(
            "approved plan allowance",
            topology.Message,
            StringComparison.Ordinal);

        using (WordprocessingDocument package =
               WordprocessingDocument.Open(changed, true))
        {
            package.MainDocumentPart!.Document!.Body!
                .Descendants<W.Text>()
                .First()
                .Text = "Undeclared content mutation";
            package.MainDocumentPart.Document.Save();
        }

        IntegrityReport tampered = ContentIntegrityValidator.Compare(
            source,
            changed,
            [IntegrityCheckIds.SectionTopology]);
        Assert.Equal(IntegrityStatus.Failed, tampered.Status);
        Assert.Equal(
            IntegrityStatus.Failed,
            Assert.Single(
                tampered.Checks,
                item => item.CheckId == "normalized_body_text")
                .Status);
    }

    private static void ApplyTamper(string path, TamperKind tamper)
    {
        using WordprocessingDocument package =
            WordprocessingDocument.Open(path, true);
        MainDocumentPart main = package.MainDocumentPart!;
        W.Document document = main.Document!;
        W.Body body = document.Body!;
        switch (tamper)
        {
            case TamperKind.BodyText:
                body.Descendants<W.Text>().First().Text =
                    "Changed title";
                document.Save();
                break;
            case TamperKind.TableText:
                body.Descendants<W.TableCell>()
                    .First()
                    .Descendants<W.Text>()
                    .First()
                    .Text = "Changed cell";
                document.Save();
                break;
            case TamperKind.Image:
                main.DeletePart(main.ImageParts.First());
                break;
            case TamperKind.Equation:
                body.Descendants<OfficeMath>()
                    .First()
                    .Remove();
                document.Save();
                break;
            case TamperKind.Hyperlink:
                body.Descendants<W.Hyperlink>()
                    .First()
                    .Anchor = "changed-anchor";
                document.Save();
                break;
            case TamperKind.Bookmark:
                body.Descendants<W.BookmarkStart>()
                    .First()
                    .Name = "ChangedBookmark";
                document.Save();
                break;
            case TamperKind.Field:
                body.Descendants<W.FieldCode>()
                    .First()
                    .Text = " REF ChangedBookmark ";
                document.Save();
                break;
            case TamperKind.Footnote:
                W.Footnotes footnotes =
                    main.FootnotesPart!.Footnotes!;
                footnotes
                    .Descendants<W.Text>()
                    .Last()
                    .Text = "Changed footnote";
                footnotes.Save();
                break;
            case TamperKind.Endnote:
                W.Endnotes endnotes =
                    main.EndnotesPart!.Endnotes!;
                endnotes
                    .Descendants<W.Text>()
                    .Last()
                    .Text = "Changed endnote";
                endnotes.Save();
                break;
            case TamperKind.Header:
                W.Header header = main.HeaderParts.First().Header!;
                header
                    .Descendants<W.Text>()
                    .First()
                    .Text = "Changed header";
                header.Save();
                break;
            case TamperKind.Footer:
                W.Footer footer = main.FooterParts.First().Footer!;
                footer
                    .Descendants<W.Text>()
                    .First()
                    .Text = "Changed footer";
                footer.Save();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(tamper),
                    tamper,
                    null);
        }
    }

    private static string CopyFixture()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "paperformat-integrity-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "changed.docx");
        File.Copy(Fixture("integrity-rich.docx"), path);
        return path;
    }

    private static string CopyOf(string source)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "paperformat-integrity-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "changed.docx");
        File.Copy(source, path);
        return path;
    }

    private static void AddTrackedInsertion(string path)
    {
        using WordprocessingDocument package =
            WordprocessingDocument.Open(path, true);
        W.Paragraph paragraph = package.MainDocumentPart!.Document!.Body!
            .Elements<W.Paragraph>()
            .First();
        paragraph.Append(
            new W.InsertedRun(
                new W.Run(new W.Text("Tracked insertion")))
            {
                Id = "42",
                Author = "PaperFormat test",
                Date = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
        package.MainDocumentPart.Document.Save();
    }

    private static void AddTrackedParagraphPropertyChange(string path)
    {
        using WordprocessingDocument package =
            WordprocessingDocument.Open(path, true);
        W.Paragraph paragraph = package.MainDocumentPart!.Document!.Body!
            .Elements<W.Paragraph>()
            .First();
        paragraph.ParagraphProperties ??= new W.ParagraphProperties();
        paragraph.ParagraphProperties.Append(
            new W.ParagraphPropertiesChange(
                new W.PreviousParagraphProperties(
                    new W.Justification
                    {
                        Val = W.JustificationValues.Left,
                    }))
            {
                Id = "43",
                Author = "PaperFormat test",
                Date = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
        package.MainDocumentPart.Document.Save();
    }

    private static void AddNumbering(string path)
    {
        using WordprocessingDocument package =
            WordprocessingDocument.Open(path, true);
        MainDocumentPart main = package.MainDocumentPart!;
        NumberingDefinitionsPart numberingPart =
            main.NumberingDefinitionsPart
            ?? main.AddNewPart<NumberingDefinitionsPart>();
        numberingPart.Numbering = new W.Numbering(
            new W.AbstractNum(
                new W.MultiLevelType
                {
                    Val = W.MultiLevelValues.SingleLevel,
                },
                new W.Level(
                    new W.StartNumberingValue { Val = 1 },
                    new W.NumberingFormat
                    {
                        Val = W.NumberFormatValues.Decimal,
                    },
                    new W.LevelText { Val = "%1." })
                {
                    LevelIndex = 0,
                })
            {
                AbstractNumberId = 1,
            },
            new W.NumberingInstance(
                new W.AbstractNumId { Val = 1 })
            {
                NumberID = 1,
            });
        W.Paragraph paragraph = main.Document!.Body!
            .Elements<W.Paragraph>()
            .First();
        paragraph.ParagraphProperties ??= new W.ParagraphProperties();
        paragraph.ParagraphProperties.NumberingProperties =
            new W.NumberingProperties(
                new W.NumberingLevelReference { Val = 0 },
                new W.NumberingId { Val = 1 });
        main.Document.Save();
        numberingPart.Numbering.Save();
    }

    private static string Fixture(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    public enum TamperKind
    {
        BodyText,
        TableText,
        Image,
        Equation,
        Hyperlink,
        Bookmark,
        Field,
        Footnote,
        Endnote,
        Header,
        Footer,
    }
}
