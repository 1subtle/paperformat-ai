using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using PaperFormat.Checking;
using PaperFormat.Classification;
using PaperFormat.Domain;
using PaperFormat.OpenXml;
using PaperFormat.Repair;
using PaperFormat.Rules;
using M = DocumentFormat.OpenXml.Math;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace PaperFormat.Repair.Tests;

public sealed class SafeRepairServiceTests
{
    [Fact]
    public void SafeRepairsUseANewFileAndPassIntegrityAndRecheck()
    {
        string source = Fixture("wrong-format.docx");
        string sourceHash = FileHash(source);
        string output = OutputPath();
        (RulePackage rules, CheckReport preCheck) = Check(source);
        string[] selected = preCheck.Issues
            .Where(
                issue => issue.AutoFixable
                    && issue.ElementType != RuleTarget.Page)
            .Select(issue => issue.IssueId)
            .ToArray();

        RepairResult result = SafeRepairService.Execute(
            source,
            output,
            rules,
            preCheck,
            new RepairSelection(selected));

        Assert.True(
            result.IsReadyForUse,
            $"original={result.ChangeLog.OriginalPreserved}; " +
            $"reopened={result.ChangeLog.OutputReopened}; " +
            $"package={result.ChangeLog.PackageValid}; " +
            $"integrity={result.Integrity.Status}; " +
            $"statuses={string.Join(',', result.ChangeLog.Entries.Select(entry => entry.Status))}");
        Assert.True(result.ChangeLog.OriginalPreserved);
        Assert.True(result.ChangeLog.OutputReopened);
        Assert.True(result.ChangeLog.PackageValid);
        Assert.Equal(IntegrityStatus.Passed, result.Integrity.Status);
        Assert.All(
            result.ChangeLog.Entries,
            entry =>
            {
                Assert.Equal(
                    RepairExecutionStatus.Applied,
                    entry.Status);
                Assert.Equal(
                    RepairAuthorization.SafeAutomatic,
                    entry.Authorization);
            });
        Assert.Equal(sourceHash, FileHash(source));
        Assert.True(File.Exists(output));
        Assert.DoesNotContain(
            result.PostRepairCheck.Issues,
            issue => selected.Contains(issue.IssueId, StringComparer.Ordinal));
        Assert.True(
            result.PostRepairCheck.Summary.IssueCount
            < preCheck.Summary.IssueCount);
    }

    [Fact]
    public void PageRepairsRequireExplicitConfirmation()
    {
        string source = Fixture("wrong-format.docx");
        (RulePackage rules, CheckReport preCheck) = Check(source);
        CheckIssue pageWidth = Assert.Single(
            preCheck.Issues,
            issue => issue.ElementType == RuleTarget.Page
                && issue.RuleId.EndsWith(
                    ".page-width",
                    StringComparison.Ordinal));
        string output = OutputPath();

        RepairResult result = SafeRepairService.Execute(
            source,
            output,
            rules,
            preCheck,
            new RepairSelection([pageWidth.IssueId]));

        ChangeLogEntry entry = Assert.Single(result.ChangeLog.Entries);
        Assert.Equal(RepairExecutionStatus.Skipped, entry.Status);
        Assert.False(result.IsReadyForUse);
        Assert.Contains(
            result.PostRepairCheck.Issues,
            issue => issue.IssueId == pageWidth.IssueId);
    }

    [Fact]
    public void ConfirmedPageRepairsAreAppliedAndDisappearOnRecheck()
    {
        string source = Fixture("wrong-format.docx");
        (RulePackage rules, CheckReport preCheck) = Check(source);
        string[] selected = preCheck.Issues
            .Where(issue => issue.ElementType == RuleTarget.Page)
            .Where(issue => issue.AutoFixable)
            .Select(issue => issue.IssueId)
            .ToArray();
        string output = OutputPath();

        RepairResult result = SafeRepairService.Execute(
            source,
            output,
            rules,
            preCheck,
            new RepairSelection(selected, pageChangesConfirmed: true));

        Assert.True(result.IsReadyForUse);
        Assert.All(
            result.ChangeLog.Entries,
            entry =>
            {
                Assert.Equal(
                    RepairExecutionStatus.Applied,
                    entry.Status);
                Assert.Equal(
                    RepairAuthorization.UserConfirmed,
                    entry.Authorization);
            });
        Assert.DoesNotContain(
            result.PostRepairCheck.Issues,
            issue => selected.Contains(issue.IssueId, StringComparer.Ordinal));
    }

    [Fact]
    public void ParagraphStyleIsProtectedWhileIndentRepairWorksOnARealDocx()
    {
        string mutated = OutputPath("mutated.docx");
        File.Copy(Fixture("valid-ieee-like.docx"), mutated);
        using (WordprocessingDocument package =
               WordprocessingDocument.Open(mutated, true))
        {
            W.Document wordDocument =
                package.MainDocumentPart!.Document!;
            W.Paragraph body = wordDocument.Body!
                .Elements<W.Paragraph>()
                .First(
                    paragraph => paragraph.InnerText.StartsWith(
                        "Each package uses",
                        StringComparison.Ordinal));
            body.ParagraphProperties!.ParagraphStyleId =
                new W.ParagraphStyleId { Val = "Normal" };
            body.ParagraphProperties.Indentation =
                new W.Indentation { FirstLine = "240" };
            wordDocument.Save();
        }

        RulePackage rules = new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));
        DocumentParseResult parsed = WordDocumentParser.Parse(mutated);
        DocumentModel document = Assert.IsType<DocumentModel>(parsed.Document);
        ParagraphModel changedParagraph = document.Sections
            .SelectMany(section => section.Paragraphs)
            .First(
                paragraph => paragraph.Text.Value.StartsWith(
                    "Each package uses",
                    StringComparison.Ordinal));
        ClassificationSet initial =
            new DeterministicDocumentClassifier().Classify(document);
        DocumentElement changedElement = Assert.Single(
            initial.Elements,
            element => element.Location == changedParagraph.Location);
        ClassificationSet classifications = ClassificationEditor.Apply(
            initial,
            [
                new ClassificationOverride(
                    changedElement.ElementId,
                    ManuscriptElementKind.Body),
            ]);
        CheckReport preCheck = new FormatCheckEngine().Check(
            document,
            rules,
            classifications);
        CheckIssue style = Assert.Single(
            preCheck.Issues,
            issue => issue.ElementType == RuleTarget.Body
                && issue.RuleId.EndsWith(
                    ".paragraph-style-id",
                    StringComparison.Ordinal));
        CheckIssue indent = Assert.Single(
            preCheck.Issues,
            issue => issue.DocumentLocation == changedParagraph.Location
                && issue.ElementType == RuleTarget.Body
                && issue.RuleId.EndsWith(
                    ".first-line-indent",
                    StringComparison.Ordinal));
        Assert.False(style.AutoFixable);
        Assert.True(indent.AutoFixable);
        string output = OutputPath();

        RepairResult result = SafeRepairService.Execute(
            mutated,
            output,
            rules,
            preCheck,
            new RepairSelection([indent.IssueId]));

        Assert.True(result.IsReadyForUse);
        Assert.DoesNotContain(
            result.PostRepairCheck.Issues,
            issue => issue.IssueId == indent.IssueId);
        using WordprocessingDocument repaired =
            WordprocessingDocument.Open(output, false);
        W.Paragraph repairedParagraph = repaired.MainDocumentPart!.Document!
            .Body!
            .Elements<W.Paragraph>()
            .First(
                paragraph => paragraph.InnerText.StartsWith(
                    "Each package uses",
                    StringComparison.Ordinal));
        Assert.Equal(
            "Normal",
            repairedParagraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value);
        Assert.Equal(
            "289",
            repairedParagraph.ParagraphProperties?.Indentation?
                .FirstLine?
                .Value);
    }

    [Fact]
    public void RepeatedRepairsHaveDeterministicSemanticResults()
    {
        string source = Fixture("wrong-format.docx");
        (RulePackage rules, CheckReport preCheck) = Check(source);
        string[] selected = preCheck.Issues
            .Where(
                issue => issue.AutoFixable
                    && issue.ElementType != RuleTarget.Page)
            .Select(issue => issue.IssueId)
            .ToArray();

        RepairResult first = SafeRepairService.Execute(
            source,
            OutputPath(),
            rules,
            preCheck,
            new RepairSelection(selected));
        RepairResult second = SafeRepairService.Execute(
            source,
            OutputPath(),
            rules,
            preCheck,
            new RepairSelection(selected));

        Assert.Equal(
            first.ChangeLog.OperationId,
            second.ChangeLog.OperationId);
        Assert.Equal(first.ChangeLog.Entries, second.ChangeLog.Entries);
        Assert.Equal(first.PostRepairCheck, second.PostRepairCheck);
        Assert.Equal(first.Integrity, second.Integrity);
        Assert.Equal(
            first.ChangeLog.OutputSha256,
            second.ChangeLog.OutputSha256);
    }

    [Fact]
    public void RepairNeverOverwritesSourceOrExistingOutput()
    {
        string source = Fixture("wrong-format.docx");
        (RulePackage rules, CheckReport preCheck) = Check(source);
        var selection = new RepairSelection(Array.Empty<string>());

        Assert.Throws<ArgumentException>(
            () => SafeRepairService.Execute(
                source,
                source,
                rules,
                preCheck,
                selection));
        string existing = OutputPath();
        File.WriteAllText(existing, "existing");
        Assert.Throws<IOException>(
            () => SafeRepairService.Execute(
                source,
                existing,
                rules,
                preCheck,
                selection));
        Assert.Equal("existing", File.ReadAllText(existing));
    }

    [Fact]
    public void PreExistingSchemaErrorsDoNotBlockAnOtherwiseSafeOutput()
    {
        string source = OutputPath("baseline-invalid.docx");
        File.Copy(Fixture("valid-ieee-like.docx"), source);
        using (WordprocessingDocument package =
               WordprocessingDocument.Open(source, true))
        {
            W.Paragraph paragraph = package.MainDocumentPart!.Document!.Body!
                .Elements<W.Paragraph>()
                .First();
            paragraph.SetAttribute(
                new DocumentFormat.OpenXml.OpenXmlAttribute(
                    "w",
                    "unsupportedFlag",
                    "http://schemas.openxmlformats.org/wordprocessingml/2006/main",
                    "1"));
            package.MainDocumentPart.Document.Save();
        }

        (RulePackage rules, CheckReport preCheck) = Check(source);
        RepairResult result = SafeRepairService.Execute(
            source,
            OutputPath(),
            rules,
            preCheck,
            new RepairSelection(Array.Empty<string>()));

        Assert.True(result.ChangeLog.PackageValid);
        Assert.True(result.IsReadyForUse);
        Assert.Equal(IntegrityStatus.Passed, result.Integrity.Status);
    }

    [Fact]
    public void MissingTargetStyleIsReportedButNeverMaterializedAutomatically()
    {
        string source = Fixture("valid-ieee-like.docx");
        string sourceHash = FileHash(source);
        RulePackage builtIn = new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));
        FormatRule bodyStyleRule = builtIn.Rules.First(
            rule => rule.Target == RuleTarget.Body
                && rule.Property == FormatProperty.ParagraphStyleId);
        RulePackage rules = RulePackageEditor.Apply(
            builtIn,
            [
                new RuleOverride(
                    bodyStyleRule.RuleId,
                    new TextRuleValue("MissingStyle")),
            ]);
        CheckReport preCheck = Check(source, rules).Report;
        CheckIssue[] issues = preCheck.Issues
            .Where(item => item.RuleId == bodyStyleRule.RuleId)
            .ToArray();
        Assert.NotEmpty(issues);
        Assert.All(issues, issue => Assert.False(issue.AutoFixable));
        string output = OutputPath();

        RepairResult result = SafeRepairService.Execute(
            source,
            output,
            rules,
            preCheck,
            new RepairSelection(issues.Select(issue => issue.IssueId)));

        Assert.All(
            result.ChangeLog.Entries,
            entry => Assert.Equal(
                RepairExecutionStatus.Skipped,
                entry.Status));
        Assert.False(result.IsReadyForUse);
        Assert.Equal(IntegrityStatus.Passed, result.Integrity.Status);
        Assert.Contains(
            result.PostRepairCheck.Issues,
            item => item.RuleId == bodyStyleRule.RuleId);
        Assert.Equal(sourceHash, FileHash(source));

        using WordprocessingDocument package =
            WordprocessingDocument.Open(output, false);
        Assert.DoesNotContain(
            package.MainDocumentPart!.StyleDefinitionsPart!.Styles!
                .Elements<W.Style>(),
            item => item.StyleId?.Value == "MissingStyle");
    }

    [Fact]
    public void SharedMissingStyleRemainsProtectedAcrossTargets()
    {
        string source = Fixture("valid-ieee-like.docx");
        RulePackage builtIn = new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));
        FormatRule[] sharedStyleRules = builtIn.Rules
            .Where(
                rule => rule.Target is
                    RuleTarget.Body
                    or RuleTarget.ReferenceEntry)
            .Where(rule => rule.Property == FormatProperty.ParagraphStyleId)
            .ToArray();
        RulePackage rules = RulePackageEditor.Apply(
            builtIn,
            sharedStyleRules.Select(
                rule => new RuleOverride(
                    rule.RuleId,
                    new TextRuleValue("SharedMissing"))));
        CheckReport preCheck = Check(source, rules).Report;
        CheckIssue[] styleIssues = preCheck.Issues
            .Where(
                issue => sharedStyleRules.Any(
                    rule => rule.RuleId == issue.RuleId))
            .ToArray();
        Assert.NotEmpty(styleIssues);

        string output = OutputPath();
        RepairResult result = SafeRepairService.Execute(
            source,
            output,
            rules,
            preCheck,
            new RepairSelection(
                styleIssues.Select(issue => issue.IssueId)));

        Assert.False(result.IsReadyForUse);
        Assert.All(
            result.ChangeLog.Entries,
            entry => Assert.Equal(
                RepairExecutionStatus.Skipped,
                entry.Status));
        Assert.Contains(
            result.PostRepairCheck.Issues,
            issue => sharedStyleRules.Any(
                rule => rule.RuleId == issue.RuleId));

        using WordprocessingDocument package =
            WordprocessingDocument.Open(output, false);
        Assert.DoesNotContain(
            package.MainDocumentPart!.StyleDefinitionsPart!.Styles!
                .Elements<W.Style>(),
            item => item.StyleId?.Value == "SharedMissing");
    }

    [Fact]
    public void MissingStylesPartIsNotSynthesizedByAutomaticRepair()
    {
        string source = OutputPath("without-styles.docx");
        File.Copy(Fixture("valid-ieee-like.docx"), source);
        using (WordprocessingDocument package =
               WordprocessingDocument.Open(source, true))
        {
            W.Paragraph abstractParagraph =
                package.MainDocumentPart!.Document!.Body!
                    .Elements<W.Paragraph>()
                    .First(
                        paragraph => paragraph.InnerText.StartsWith(
                            "Abstract",
                            StringComparison.Ordinal));
            abstractParagraph.ParagraphProperties!.ParagraphStyleId =
                new W.ParagraphStyleId { Val = "Normal" };
            package.MainDocumentPart.Document.Save();
            StyleDefinitionsPart styles =
                package.MainDocumentPart!.StyleDefinitionsPart!;
            package.MainDocumentPart.DeletePart(styles);
        }

        RulePackage rules = new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));
        DocumentModel document = Assert.IsType<DocumentModel>(
            WordDocumentParser.Parse(source).Document);
        ParagraphModel abstractModel = document.Sections
            .SelectMany(section => section.Paragraphs)
            .First(
                paragraph => paragraph.Text.Value.StartsWith(
                    "Abstract",
                    StringComparison.Ordinal));
        ClassificationSet initial =
            new DeterministicDocumentClassifier().Classify(document);
        DocumentElement abstractElement = Assert.Single(
            initial.Elements,
            element => element.Location == abstractModel.Location);
        ClassificationSet classifications = ClassificationEditor.Apply(
            initial,
            [
                new ClassificationOverride(
                    abstractElement.ElementId,
                    ManuscriptElementKind.Abstract),
            ]);
        CheckReport preCheck = new FormatCheckEngine().Check(
            document,
            rules,
            classifications);
        CheckIssue abstractStyle = Assert.Single(
            preCheck.Issues,
            issue => issue.ElementType == RuleTarget.Abstract
                && issue.RuleId.EndsWith(
                    ".paragraph-style-id",
                    StringComparison.Ordinal));
        Assert.False(abstractStyle.AutoFixable);
        string output = OutputPath();

        RepairResult result = SafeRepairService.Execute(
            source,
            output,
            rules,
            preCheck,
            new RepairSelection([abstractStyle.IssueId]));

        Assert.False(result.IsReadyForUse);
        Assert.Equal(IntegrityStatus.Passed, result.Integrity.Status);
        using WordprocessingDocument repaired =
            WordprocessingDocument.Open(output, false);
        Assert.Null(repaired.MainDocumentPart!.StyleDefinitionsPart);
    }

    [Fact]
    public void RepairsAfterInlineMathUseTheCorrectWordRunLocation()
    {
        string source = OutputPath("inline-math.docx");
        File.Copy(Fixture("valid-ieee-like.docx"), source);
        using (WordprocessingDocument package =
               WordprocessingDocument.Open(source, true))
        {
            W.Paragraph paragraph = package.MainDocumentPart!.Document!.Body!
                .Elements<W.Paragraph>()
                .First(
                    item => item.InnerText.StartsWith(
                        "Each package uses",
                        StringComparison.Ordinal));
            paragraph.RemoveAllChildren<W.Run>();
            paragraph.Append(
                new W.Run(new W.Text("Before ")),
                new M.OfficeMath(
                    new M.Run(new M.Text("x + y"))),
                new W.Run(
                    new W.RunProperties(
                        new W.RunFonts
                        {
                            Ascii = "Courier New",
                            HighAnsi = "Courier New",
                        },
                        new W.FontSize { Val = "28" }),
                    new W.Text("after")));
            package.MainDocumentPart.Document.Save();
        }

        RulePackage rules = new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));
        DocumentModel document = Assert.IsType<DocumentModel>(
            WordDocumentParser.Parse(source).Document);
        ParagraphModel mixedParagraph = Assert.Single(
            document.Sections.SelectMany(section => section.Paragraphs),
            paragraph => paragraph.Text.Value.Contains(
                "Before",
                StringComparison.Ordinal));
        ClassificationSet initial =
            new DeterministicDocumentClassifier().Classify(document);
        DocumentElement mixedElement = Assert.Single(
            initial.Elements,
            element => element.Location == mixedParagraph.Location);
        ClassificationSet classifications = ClassificationEditor.Apply(
            initial,
            [
                new ClassificationOverride(
                    mixedElement.ElementId,
                    ManuscriptElementKind.Body),
            ]);
        CheckReport preCheck = new FormatCheckEngine().Check(
            document,
            rules,
            classifications);
        CheckIssue[] selectedIssues = preCheck.Issues
            .Where(issue => issue.AutoFixable)
            .Where(issue => issue.DocumentLocation.RunIndex == 1)
            .ToArray();
        Assert.NotEmpty(selectedIssues);

        RepairResult result = SafeRepairService.Execute(
            source,
            OutputPath(),
            rules,
            preCheck,
            new RepairSelection(
                selectedIssues.Select(issue => issue.IssueId)));

        Assert.True(result.IsReadyForUse);
        Assert.Equal(IntegrityStatus.Passed, result.Integrity.Status);
        Assert.All(
            result.ChangeLog.Entries,
            entry => Assert.Equal(
                RepairExecutionStatus.Applied,
                entry.Status));
    }

    private static (RulePackage Rules, CheckReport Report) Check(string path)
    {
        RulePackage rules = new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));
        DocumentParseResult parsed = WordDocumentParser.Parse(path);
        DocumentModel document = Assert.IsType<DocumentModel>(parsed.Document);
        ClassificationSet classifications =
            new DeterministicDocumentClassifier().Classify(document);
        CheckReport report = new FormatCheckEngine().Check(
            document,
            rules,
            classifications);
        return (rules, report);
    }

    private static (RulePackage Rules, CheckReport Report) Check(
        string path,
        RulePackage rules)
    {
        DocumentParseResult parsed = WordDocumentParser.Parse(path);
        DocumentModel document = Assert.IsType<DocumentModel>(parsed.Document);
        ClassificationSet classifications =
            new DeterministicDocumentClassifier().Classify(document);
        return (
            rules,
            new FormatCheckEngine().Check(
                document,
                rules,
                classifications));
    }

    private static string Fixture(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static string OutputPath(string fileName = "repaired.docx")
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "paperformat-repair-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    private static string FileHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
