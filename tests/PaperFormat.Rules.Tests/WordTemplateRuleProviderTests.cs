using PaperFormat.Domain;
using PaperFormat.OpenXml;
using PaperFormat.Rules;

namespace PaperFormat.Rules.Tests;

public sealed class WordTemplateRuleProviderTests
{
    [Fact]
    public void ExtractsPageStylesAndEffectiveFormattingFromRealDocx()
    {
        DocumentModel document = Parse("valid-ieee-like.docx");
        var provider = new WordTemplateRuleProvider();

        RulePackage package = provider.Extract(
            new WordTemplateFormatRequirementSource(
                "valid-ieee-like.docx",
                document));

        Assert.Equal(
            new Twip(12_240),
            TwipValue(package, RuleTarget.Page, FormatProperty.PageWidth));
        Assert.Equal(
            2,
            Assert.IsType<IntegerRuleValue>(
                Rule(
                    package,
                    RuleTarget.Page,
                    FormatProperty.ColumnCount).Expected).Value);
        Assert.DoesNotContain(
            package.Rules,
            rule => rule.Property == FormatProperty.ParagraphStyleId);
        Assert.Equal(
            "Times New Roman",
            TextValue(package, RuleTarget.Body, FormatProperty.FontAscii));
        Assert.Equal(
            Twip.FromPoints(10m),
            TwipValue(package, RuleTarget.Body, FormatProperty.FontSize));
        Assert.Equal(
            Twip.FromPoints(8m),
            TwipValue(package, RuleTarget.TableText, FormatProperty.FontSize));
        Assert.DoesNotContain(
            package.Rules,
            rule => rule.Target == RuleTarget.TableText
                && rule.Property is
                    FormatProperty.Bold or FormatProperty.Italic);
        Assert.DoesNotContain(
            package.Rules,
            rule => rule.Target == RuleTarget.TableText
                && rule.Property is
                    FormatProperty.ParagraphAlignment
                    or FormatProperty.LineSpacing
                    or FormatProperty.SpaceBefore
                    or FormatProperty.SpaceAfter
                    or FormatProperty.FirstLineIndent);
        Assert.All(
            package.Rules,
            rule => Assert.NotEqual(
                RuleEvidenceKind.BuiltIn,
                rule.Evidence.Kind));
    }

    [Fact]
    public void ExtractionIsDeterministicAndNeverSerializesTemplateText()
    {
        DocumentModel document = Parse("valid-ieee-like.docx");
        var provider = new WordTemplateRuleProvider();
        var source = new WordTemplateFormatRequirementSource(
            "/private/path/valid-ieee-like.docx",
            document);

        string first = RulePackageJson.Serialize(provider.Extract(source));
        string second = RulePackageJson.Serialize(provider.Extract(source));

        Assert.Equal(first, second);
        Assert.DoesNotContain(
            "synthetic manuscript exercises",
            first,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/private/path", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ConflictingSectionsProduceAnExplicitNoticeInsteadOfARule()
    {
        DocumentModel source = Parse("valid-ieee-like.docx");
        SectionModel first = source.Sections[0];
        var second = new SectionModel(
            new StructuralLocation(
                DocumentPartKind.MainDocument,
                sectionIndex: 1),
            first.PageSettings with
            {
                Columns = new Columns(count: 1),
            },
            Array.Empty<ParagraphModel>(),
            Array.Empty<TableModel>());
        var conflicting = new DocumentModel(
            source.PackageKind,
            source.Defaults,
            source.Styles,
            [first, second]);

        RulePackage package = new WordTemplateRuleProvider().Extract(
            new WordTemplateFormatRequirementSource(
                "conflicting.docx",
                conflicting));

        Assert.DoesNotContain(
            package.Rules,
            rule => rule.Target == RuleTarget.Page
                && rule.Property == FormatProperty.ColumnCount);
        Assert.Contains(
            package.Notices,
            notice => notice.Code
                == "template.page.column-count.ambiguous");
    }

    [Fact]
    public void PositionalAuthorAndAffiliationRulesAreUsableForFormatting()
    {
        RulePackage package = new WordTemplateRuleProvider().Extract(
            new WordTemplateFormatRequirementSource(
                "valid-ieee-like.docx",
                Parse("valid-ieee-like.docx")));

        Assert.All(
            package.Rules.Where(
                rule => rule.Target is
                    RuleTarget.Author or RuleTarget.Affiliation),
            rule => Assert.False(rule.NeedsConfirmation));
        Assert.All(
            package.Rules.Where(
                rule => rule.Target is
                    RuleTarget.Abstract or RuleTarget.Keywords),
            rule => Assert.False(rule.NeedsConfirmation));
    }

    [Fact]
    public void DotxFilenameProducesATemplatePackageAndExtractedRules()
    {
        using FileStream stream = File.OpenRead(Fixture("valid-ieee-like.docx"));
        DocumentParseResult parsed = WordDocumentParser.Parse(
            stream,
            "target.dotx");
        DocumentModel document = Assert.IsType<DocumentModel>(parsed.Document);

        RulePackage package = new WordTemplateRuleProvider().Extract(
            new WordTemplateFormatRequirementSource("target.dotx", document));

        Assert.True(parsed.IsSuccess);
        Assert.Equal(WordPackageKind.Template, document.PackageKind);
        Assert.NotEmpty(package.Rules);
    }

    [Fact]
    public void GeneratedSyntheticFixtureProducesAUsablePackageWithoutBodyText()
    {
        DocumentModel document = Parse("valid-ieee-like.docx");

        RulePackage package = new WordTemplateRuleProvider().Extract(
            new WordTemplateFormatRequirementSource(
                "valid-ieee-like.docx",
                document));
        string json = RulePackageJson.Serialize(package);

        Assert.NotEmpty(package.Rules);
        Assert.Contains(
            package.Rules,
            rule => rule.Target == RuleTarget.Page);
        Assert.DoesNotContain(
            "deterministic fixture preserves",
            json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SemanticExtractionDoesNotDependOnStyleDisplayNames()
    {
        DocumentModel source = Parse("valid-ieee-like.docx");
        StyleDefinition[] opaqueStyles = source.Styles
            .Select(
                (style, index) => new StyleDefinition(
                    style.StyleId,
                    $"Opaque {index}",
                    style.Kind,
                    style.BasedOnStyleId,
                    style.LinkedStyleId,
                    style.IsDefault,
                    style.IsCustom,
                    style.ParagraphFormatting,
                    style.CharacterFormatting,
                    style.TableFormatting))
            .ToArray();
        var renamed = new DocumentModel(
            source.PackageKind,
            source.Defaults,
            opaqueStyles,
            source.Sections);

        RulePackage package = new WordTemplateRuleProvider().Extract(
            new WordTemplateFormatRequirementSource(
                "opaque-styles.docx",
                renamed));

        Assert.DoesNotContain(
            package.Rules,
            rule => rule.Property == FormatProperty.ParagraphStyleId);
        Assert.Equal(
            "Times New Roman",
            TextValue(package, RuleTarget.Body, FormatProperty.FontAscii));
        Assert.Contains(
            package.Rules,
            rule => rule.Target == RuleTarget.Heading3);
    }

    [Fact]
    public void HeadingStylesSupportUnnumberedPublisherTemplates()
    {
        DocumentModel source = Parse("valid-ieee-like.docx");
        SectionModel[] sections = source.Sections
            .Select(
                section => new SectionModel(
                    section.Location,
                    section.PageSettings,
                    section.Paragraphs.Select(WithoutHeadingNumber),
                    section.Tables))
            .ToArray();
        var unnumbered = new DocumentModel(
            source.PackageKind,
            source.Defaults,
            source.Styles,
            sections);

        RulePackage package = new WordTemplateRuleProvider().Extract(
            new WordTemplateFormatRequirementSource(
                "unnumbered-publisher-template.docx",
                unnumbered));

        Assert.Contains(
            package.Rules,
            rule => rule.Target == RuleTarget.Heading1);
        Assert.Contains(
            package.Rules,
            rule => rule.Target == RuleTarget.Heading2);
        Assert.Contains(
            package.Rules,
            rule => rule.Target == RuleTarget.Heading3);
    }

    private static DocumentModel Parse(string fileName)
    {
        DocumentParseResult result = WordDocumentParser.Parse(Fixture(fileName));
        Assert.True(result.IsSuccess);
        return Assert.IsType<DocumentModel>(result.Document);
    }

    private static ParagraphModel WithoutHeadingNumber(
        ParagraphModel paragraph)
    {
        string text = paragraph.StyleId switch
        {
            "Heading1" => "Introduction",
            "Heading2" => "Related work",
            "Heading3" => "Data collection",
            _ => paragraph.Text.Value,
        };
        return new ParagraphModel(
            paragraph.Location,
            paragraph.BlockIndex,
            paragraph.StyleId,
            new DocumentText(text),
            paragraph.DirectFormatting,
            paragraph.EffectiveFormatting,
            paragraph.Runs);
    }

    private static FormatRule Rule(
        RulePackage package,
        RuleTarget target,
        FormatProperty property) =>
        Assert.Single(
            package.Rules,
            rule => rule.Target == target && rule.Property == property);

    private static Twip TwipValue(
        RulePackage package,
        RuleTarget target,
        FormatProperty property) =>
        Assert.IsType<TwipRuleValue>(
            Rule(package, target, property).Expected).Value;

    private static string TextValue(
        RulePackage package,
        RuleTarget target,
        FormatProperty property) =>
        Assert.IsType<TextRuleValue>(
            Rule(package, target, property).Expected).Value;

    private static string Fixture(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
