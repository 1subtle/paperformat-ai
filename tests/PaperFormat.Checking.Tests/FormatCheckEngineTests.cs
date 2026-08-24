using PaperFormat.Checking;
using PaperFormat.Classification;
using PaperFormat.Domain;
using PaperFormat.OpenXml;
using PaperFormat.Rules;

namespace PaperFormat.Checking.Tests;

public sealed class FormatCheckEngineTests
{
    [Fact]
    public void ValidFixtureHasNoSupportedFormatIssues()
    {
        CheckReport report = Check("valid-ieee-like.docx");

        Assert.Equal(CheckStatus.Passed, report.Status);
        Assert.Empty(report.Issues);
        Assert.Equal(100, report.Summary.Score);
        Assert.True(report.Summary.EvaluatedObservations > 100);
        Assert.All(
            report.SkippedRules,
            item => Assert.Equal("no_matching_element", item.ReasonCode));
    }

    [Fact]
    public void WrongFixtureReportsEveryIntentionalDeviationCategory()
    {
        CheckReport report = Check("wrong-format.docx");

        Assert.Equal(CheckStatus.IssuesFound, report.Status);
        Assert.True(report.Summary.IssueCount >= 25);
        AssertIssue(report, RuleTarget.Page, FormatProperty.PageWidth);
        AssertIssue(report, RuleTarget.Page, FormatProperty.PageHeight);
        AssertIssue(report, RuleTarget.Page, FormatProperty.PageOrientation);
        AssertIssue(report, RuleTarget.Page, FormatProperty.MarginLeft);
        AssertIssue(report, RuleTarget.Page, FormatProperty.ColumnCount);
        AssertIssue(report, RuleTarget.Page, FormatProperty.ColumnSpacing);
        AssertIssue(report, RuleTarget.Body, FormatProperty.FontAscii);
        AssertIssue(report, RuleTarget.Body, FormatProperty.FontSize);
        AssertIssue(report, RuleTarget.Body, FormatProperty.ParagraphAlignment);
        AssertIssue(report, RuleTarget.Body, FormatProperty.LineSpacing);
        AssertIssue(report, RuleTarget.Body, FormatProperty.SpaceAfter);
        AssertIssue(
            report,
            RuleTarget.Body,
            FormatProperty.DirectFormattingConsistency);
        AssertIssue(report, RuleTarget.Heading1, FormatProperty.FontSize);
        AssertIssue(report, RuleTarget.Heading2, FormatProperty.Italic);
        AssertIssue(
            report,
            RuleTarget.FigureCaption,
            FormatProperty.ParagraphAlignment);
        AssertIssue(
            report,
            RuleTarget.TableCaption,
            FormatProperty.FontAscii);
        AssertIssue(report, RuleTarget.TableText, FormatProperty.FontSize);
    }

    [Fact]
    public void RepeatedChecksProduceStableIssueAndReportIdentifiers()
    {
        CheckReport first = Check("wrong-format.docx");
        CheckReport second = Check("wrong-format.docx");

        Assert.Equal(first, second);
        Assert.Equal(
            first.Issues.Select(issue => issue.IssueId),
            second.Issues.Select(issue => issue.IssueId));
    }

    [Fact]
    public void PendingClassificationIsReportedAndNotEvaluated()
    {
        (DocumentModel document, ClassificationSet classifications) =
            ParseAndClassify("valid-ieee-like.docx");
        DocumentElement body = classifications.Elements.First(
            element => element.Kind == ManuscriptElementKind.Body);
        var pending = new DocumentElement(
            body.ElementId,
            body.Location,
            body.Kind,
            0.6m,
            ClassificationStatus.NeedsConfirmation,
            body.Reasons,
            body.TextLength,
            body.SourceStyleId);
        var edited = new ClassificationSet(
            classifications.Revision + 1,
            classifications.Elements.Select(
                element => element.ElementId == body.ElementId
                    ? pending
                    : element));

        CheckReport report = new FormatCheckEngine().Check(
            document,
            Rules(),
            edited);

        Assert.Equal(CheckStatus.Passed, report.Status);
        PendingElement item = Assert.Single(report.PendingElements);
        Assert.Equal(body.ElementId, item.ElementId);
        Assert.Contains(
            report.SkippedRules,
            skip => skip.ReasonCode == "classification_pending"
                && skip.RuleId.Contains(".body.", StringComparison.Ordinal));
        Assert.DoesNotContain(
            report.Issues,
            issue => issue.DocumentLocation == body.Location);
    }

    [Fact]
    public void RepairPolicyFlowsToIssueWithoutMakingAllIssuesAutoFixable()
    {
        CheckReport report = Check("wrong-format.docx");
        CheckIssue pageWidth = Assert.Single(
            report.Issues,
            issue => issue.ElementType == RuleTarget.Page
                && issue.RuleId.EndsWith(
                    ".page-width",
                    StringComparison.Ordinal));
        CheckIssue columns = Assert.Single(
            report.Issues,
            issue => issue.ElementType == RuleTarget.Page
                && issue.RuleId.EndsWith(
                    ".column-count",
                    StringComparison.Ordinal));

        Assert.True(pageWidth.AutoFixable);
        Assert.False(columns.AutoFixable);
        Assert.NotNull(pageWidth.CurrentValue);
        Assert.NotEqual(pageWidth.CurrentValue, pageWidth.ExpectedValue);
    }

    [Fact]
    public void DisabledRulesAreNotEvaluated()
    {
        (DocumentModel document, ClassificationSet classifications) =
            ParseAndClassify("wrong-format.docx");
        FormatRule source = Rules().Rules.First(
            rule => rule.Target == RuleTarget.Page
                && rule.Property == FormatProperty.PageWidth);
        var disabled = new FormatRule(
            source.RuleId,
            source.Target,
            source.Property,
            source.Expected,
            source.Severity,
            source.RepairLevel,
            source.Evidence,
            source.Confidence,
            enabled: false);
        var package = new RulePackage(
            "disabled-test",
            1,
            "Disabled rule test",
            "test",
            "1",
            "test",
            [disabled]);

        CheckReport report =
            new FormatCheckEngine().Check(document, package, classifications);

        Assert.Empty(report.Issues);
        Assert.Equal(0, report.Summary.EnabledRules);
        Assert.Equal(0, report.Summary.EvaluatedObservations);
    }

    [Fact]
    public void UnconfirmedRulesAreSkippedAndRequireConfirmation()
    {
        (DocumentModel document, ClassificationSet classifications) =
            ParseAndClassify("wrong-format.docx");
        FormatRule source = Rules().Rules.First(
            rule => rule.Target == RuleTarget.Page
                && rule.Property == FormatProperty.PageWidth);
        var unconfirmed = new FormatRule(
            source.RuleId,
            source.Target,
            source.Property,
            source.Expected,
            source.Severity,
            source.RepairLevel,
            source.Evidence,
            source.Confidence,
            needsConfirmation: true);
        var package = new RulePackage(
            "unconfirmed-test",
            1,
            "Unconfirmed rule test",
            "test",
            "1",
            "test",
            [unconfirmed]);

        CheckReport report =
            new FormatCheckEngine().Check(document, package, classifications);

        Assert.Equal(CheckStatus.NeedsConfirmation, report.Status);
        Assert.Empty(report.Issues);
        SkippedRule skipped = Assert.Single(report.SkippedRules);
        Assert.Equal("rule_needs_confirmation", skipped.ReasonCode);
    }

    private static CheckReport Check(string fileName)
    {
        (DocumentModel document, ClassificationSet classifications) =
            ParseAndClassify(fileName);
        return new FormatCheckEngine().Check(
            document,
            Rules(),
            classifications);
    }

    private static (
        DocumentModel Document,
        ClassificationSet Classifications) ParseAndClassify(string fileName)
    {
        DocumentParseResult parsed = WordDocumentParser.Parse(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
        DocumentModel document = Assert.IsType<DocumentModel>(parsed.Document);
        ClassificationSet classifications =
            new DeterministicDocumentClassifier().Classify(document);
        return (document, classifications);
    }

    private static RulePackage Rules() =>
        new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));

    private static void AssertIssue(
        CheckReport report,
        RuleTarget target,
        FormatProperty property) =>
        Assert.Contains(
            report.Issues,
            issue => issue.ElementType == target
                && issue.RuleId.EndsWith(
                    "." + PropertyId(property),
                    StringComparison.Ordinal));

    private static string PropertyId(FormatProperty property) =>
        property switch
        {
            FormatProperty.PageWidth => "page-width",
            FormatProperty.PageHeight => "page-height",
            FormatProperty.PageOrientation => "page-orientation",
            FormatProperty.MarginLeft => "margin-left",
            FormatProperty.ColumnCount => "column-count",
            FormatProperty.ColumnSpacing => "column-spacing",
            FormatProperty.FontAscii => "font-ascii",
            FormatProperty.FontSize => "font-size",
            FormatProperty.ParagraphAlignment => "paragraph-alignment",
            FormatProperty.LineSpacing => "line-spacing",
            FormatProperty.SpaceAfter => "space-after",
            FormatProperty.DirectFormattingConsistency =>
                "direct-formatting-consistency",
            FormatProperty.Italic => "italic",
            _ => throw new ArgumentOutOfRangeException(
                nameof(property),
                property,
                null),
        };
}
