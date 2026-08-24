using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;
using Json.Schema;
using PaperFormat.Ai;
using PaperFormat.Domain;
using PaperFormat.Integrity;
using PaperFormat.Layout;
using PaperFormat.OpenXml;
using PaperFormat.Rendering;
using PaperFormat.Reporting;

namespace PaperFormat.Cli.Tests;

public sealed class CliApplicationTests
{
    private static readonly ConcurrentDictionary<string, JsonSchema>
        SchemaCache = new(StringComparer.Ordinal);

    [Fact]
    public void PageCountChangeIsAdvisoryWhenNoBlockingAnomalyIsIntroduced()
    {
        using var task = new TestTask();
        string before = task.Path("before-pages");
        string after = task.Path("after-pages");
        Directory.CreateDirectory(before);
        Directory.CreateDirectory(after);
        File.WriteAllBytes(Path.Combine(before, "page-1.png"), FakeRenderer.Png);
        File.WriteAllBytes(Path.Combine(before, "page-2.png"), FakeRenderer.Png);
        File.WriteAllBytes(Path.Combine(after, "page-1.png"), FakeRenderer.Png);

        PageComparisonReport report = PageComparer.Compare(before, after);

        Assert.Equal("passed", report.Status);
        PageComparisonFinding finding = Assert.Single(
            report.Findings,
            item => item.Code == "page_count_changed");
        Assert.Equal("information", finding.Severity);
    }

    [Fact]
    public async Task InspectEmitsContentSafeVersionedArtifacts()
    {
        using var task = new TestTask();
        string artifact = task.Path("inspection.json");

        Invocation result = await InvokeAsync(
            new UnavailableRenderer(),
            "inspect",
            "--input",
            Fixture("integrity-rich.docx"),
            "--output",
            artifact);

        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        AssertSchema("cli-result.schema.json", result.Output);
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(artifact));
        JsonElement inspection =
            document.RootElement.GetProperty("inspection");
        AssertSchema(
            "document-inspection.schema.json",
            inspection.GetRawText());
        JsonElement resources = inspection.GetProperty("resources");
        Assert.Equal(1, resources.GetProperty("imageCount").GetInt32());
        Assert.Equal(1, resources.GetProperty("equationCount").GetInt32());
        Assert.Equal(1, resources.GetProperty("hyperlinkCount").GetInt32());
        Assert.DoesNotContain(
            "synthetic manuscript",
            File.ReadAllText(artifact),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckWorkflowIsDeterministicAndDoesNotMutateSource()
    {
        using var task = new TestTask();
        string source = Fixture("wrong-format.docx");
        string before = Sha256(source);
        string rules = task.Path("ieee-rules.json");
        string classifications = task.Path("classifications.json");

        Invocation derive = await InvokeAsync(
            new UnavailableRenderer(),
            "derive-template",
            "--ieee",
            "--output",
            rules);
        Invocation classify = await InvokeAsync(
            new UnavailableRenderer(),
            "classify",
            "--input",
            source,
            "--output",
            classifications);
        Invocation check = await InvokeAsync(
            new UnavailableRenderer(),
            "check",
            "--input",
            source,
            "--rules",
            rules,
            "--classifications",
            classifications,
            "--output-dir",
            task.Path("check"));

        Assert.Equal(CliExitCodes.Success, derive.ExitCode);
        Assert.Equal(CliExitCodes.Success, classify.ExitCode);
        Assert.Equal(CliExitCodes.Success, check.ExitCode);
        AssertSchema("cli-result.schema.json", check.Output);
        Assert.Equal(before, Sha256(source));
        using JsonDocument envelope = JsonDocument.Parse(check.Output);
        Assert.Equal(
            "issuesFound",
            envelope.RootElement.GetProperty("status").GetString());
        Assert.True(
            envelope.RootElement
                .GetProperty("data")
                .GetProperty("issueCount")
                .GetInt32() > 0);
        Assert.True(File.Exists(task.Path("check/issue-report.json")));
        Assert.True(File.Exists(task.Path("check/issue-report.html")));
    }

    [Fact]
    public async Task RunWorkflowCreatesPortableCheckOnlyTask()
    {
        using var task = new TestTask();
        string source = Fixture("wrong-format.docx");
        string sourceHash = Sha256(source);
        string workspace = task.Path("workflow");

        Invocation result = await InvokeAsync(
            new UnavailableRenderer(),
            "run-workflow",
            "--manuscript",
            source,
            "--ieee",
            "--workspace",
            workspace);

        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        AssertSchema("cli-result.schema.json", result.Output);
        AssertSchema(
            "workflow-manifest.schema.json",
            File.ReadAllText(Path.Combine(workspace, "workflow.json")));
        Assert.Equal(sourceHash, Sha256(source));
        Assert.Equal(
            sourceHash,
            Sha256(Path.Combine(workspace, "original.docx")));
        Assert.True(File.Exists(Path.Combine(workspace, "FINAL_STATUS.md")));
        Assert.False(File.Exists(Path.Combine(workspace, "formatted.docx")));
    }

    [Fact]
    public async Task RunWorkflowAcceptsAValidatedExternalRulePackage()
    {
        using var task = new TestTask();
        string rules = task.Path("venue-rules.json");
        Invocation derive = await InvokeAsync(
            new UnavailableRenderer(),
            "derive-template",
            "--ieee",
            "--output",
            rules);
        Assert.Equal(CliExitCodes.Success, derive.ExitCode);

        string workspace = task.Path("workflow-with-rules");
        Invocation result = await InvokeAsync(
            new UnavailableRenderer(),
            "run-workflow",
            "--manuscript",
            Fixture("wrong-format.docx"),
            "--rules",
            rules,
            "--workspace",
            workspace);

        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(workspace, "workflow.json")));
        Assert.Equal(
            "venue-rules.json",
            manifest.RootElement.GetProperty("formatSource").GetString());
        Assert.True(File.Exists(Path.Combine(workspace, "format-spec.json")));
    }

    [Fact]
    public async Task RenderAndPageCompareExposeMachineReadableEvidence()
    {
        using var task = new TestTask();
        string before = task.Path("before");
        string after = task.Path("after");
        var renderer = new FakeRenderer();

        Invocation first = await InvokeAsync(
            renderer,
            "render",
            "--input",
            Fixture("valid-ieee-like.docx"),
            "--output-dir",
            before);
        Invocation second = await InvokeAsync(
            renderer,
            "render",
            "--input",
            Fixture("valid-ieee-like.docx"),
            "--output-dir",
            after);
        string comparison = task.Path("comparison.json");
        Invocation compare = await InvokeAsync(
            renderer,
            "compare-pages",
            "--before",
            before,
            "--after",
            after,
            "--output",
            comparison);

        Assert.Equal(CliExitCodes.Success, first.ExitCode);
        Assert.Equal(CliExitCodes.Success, second.ExitCode);
        Assert.Equal(CliExitCodes.Success, compare.ExitCode);
        AssertSchema(
            "render-manifest.schema.json",
            File.ReadAllText(
                Path.Combine(before, "render-manifest.json")));
        AssertSchema(
            "page-comparison.schema.json",
            File.ReadAllText(comparison));
    }

    [Fact]
    public async Task IntegrityValidationPassesForAnUnchangedCopy()
    {
        using var task = new TestTask();
        string source = Fixture("integrity-rich.docx");
        string copy = task.Path("candidate.docx");
        File.Copy(source, copy);
        string output = task.Path("validation.json");

        Invocation result = await InvokeAsync(
            new UnavailableRenderer(),
            "validate-integrity",
            "--source",
            source,
            "--candidate",
            copy,
            "--output",
            output);

        Assert.Equal(CliExitCodes.Success, result.ExitCode);
        AssertSchema(
            "validation-report.schema.json",
            File.ReadAllText(output));
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(output));
        Assert.Equal(
            "passed",
            document.RootElement.GetProperty("status").GetString());
        Assert.True(
            document.RootElement
                .GetProperty("sourcePreserved")
                .GetBoolean());
    }

    [Fact]
    public async Task AgentPlanIsPolicyValidatedBeforeSafeAndReviewApply()
    {
        using var task = new TestTask();
        string source = Fixture("wrong-format.docx");
        string sourceHash = Sha256(source);
        string rules = task.Path("rules.json");
        string checkDirectory = task.Path("check");
        await InvokeAsync(
            new UnavailableRenderer(),
            "derive-template",
            "--ieee",
            "--output",
            rules);
        await InvokeAsync(
            new UnavailableRenderer(),
            "check",
            "--input",
            source,
            "--rules",
            rules,
            "--output-dir",
            checkDirectory);
        string report = Path.Combine(
            checkDirectory,
            "issue-report.json");
        using JsonDocument candidates = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    checkDirectory,
                    "plan-candidates.json")));
        JsonElement[] scopes = candidates.RootElement
            .GetProperty("candidateScopes")
            .EnumerateArray()
            .ToArray();
        string safeGroup = scopes.Single(
                item => item.GetProperty("ruleId").GetString()
                    == "ieee-like-v1.abstract.font-ascii")
            .GetProperty("groupId")
            .GetString()!;
        string reviewGroup = scopes.Single(
                item => item.GetProperty("ruleId").GetString()
                    == "ieee-like-v1.abstract.font-size")
            .GetProperty("groupId")
            .GetString()!;
        string reportId = candidates.RootElement
            .GetProperty("sourceReportId")
            .GetString()!;
        string sourceSha256 = candidates.RootElement
            .GetProperty("sourceSha256")
            .GetString()!;
        var proposal = new AgentPlanProposal(
            reportId,
            sourceSha256,
            "codex",
            "test-agent",
            visualEvidenceUsed: true,
            externalProcessingConsent: false,
            [
                new ProposedDirective(
                    safeGroup,
                    RepairPlanDecision.Apply,
                    RepairPlanRisk.Low,
                    0.99m,
                    "Character-only font normalization is deterministic."),
                new ProposedDirective(
                    reviewGroup,
                    RepairPlanDecision.Apply,
                    RepairPlanRisk.Medium,
                    0.98m,
                    "Font-size change requires explicit review."),
            ]);
        string proposalPath = task.Path("proposal.json");
        File.WriteAllText(
            proposalPath,
            AgentPlanProposalJson.Serialize(proposal));
        AssertSchema(
            "agent-plan-proposal.schema.json",
            File.ReadAllText(proposalPath));
        string planPath = task.Path("repair-plan.json");

        string otherSource = Fixture("valid-ieee-like.docx");
        var staleProposal = new AgentPlanProposal(
            reportId,
            Sha256(otherSource),
            "codex",
            "test-agent",
            visualEvidenceUsed: true,
            externalProcessingConsent: false,
            directives: proposal.Directives);
        string staleProposalPath = task.Path("stale-proposal.json");
        File.WriteAllText(
            staleProposalPath,
            AgentPlanProposalJson.Serialize(staleProposal));
        string stalePlanPath = task.Path("stale-plan.json");
        Invocation staleValidation = await InvokeAsync(
            new UnavailableRenderer(),
            "plan-validate",
            "--source",
            source,
            "--report",
            report,
            "--rules",
            rules,
            "--proposal",
            staleProposalPath,
            "--output",
            stalePlanPath);

        Assert.Equal(CliExitCodes.InvalidInput, staleValidation.ExitCode);
        Assert.False(File.Exists(stalePlanPath));

        Invocation validate = await InvokeAsync(
            new UnavailableRenderer(),
            "plan-validate",
            "--source",
            source,
            "--report",
            report,
            "--rules",
            rules,
            "--proposal",
            proposalPath,
            "--output",
            planPath);

        Assert.Equal(CliExitCodes.NeedsConfirmation, validate.ExitCode);
        AssertSchema("repair-plan.schema.json", File.ReadAllText(planPath));
        RepairPlan plan = RepairPlanJson.Deserialize(
            File.ReadAllText(planPath));
        RepairPlanDirective safe = Assert.Single(
            plan.Directives,
            item => item.Level == ModificationLevel.Safe);
        RepairPlanDirective review = Assert.Single(
            plan.Directives,
            item => item.Level == ModificationLevel.Review);
        Assert.False(safe.RequiresUserConfirmation);
        Assert.True(review.RequiresUserConfirmation);
        Assert.All(
            plan.Directives.Where(
                item => item.Level == ModificationLevel.Advisory),
            item => Assert.NotEqual(
                RepairPlanDecision.Apply,
                item.Decision));

        string staleApplyDirectory = task.Path("stale-apply");
        Invocation staleApply = await InvokeAsync(
            new UnavailableRenderer(),
            "apply",
            "--input",
            otherSource,
            "--rules",
            rules,
            "--report",
            report,
            "--plan",
            planPath,
            "--output-dir",
            staleApplyDirectory);
        Assert.Equal(CliExitCodes.InvalidInput, staleApply.ExitCode);
        Assert.False(
            File.Exists(
                Path.Combine(staleApplyDirectory, "original.docx")));
        Assert.False(
            File.Exists(
                Path.Combine(staleApplyDirectory, "formatted.docx")));

        string applyDirectory = task.Path("apply");
        Invocation apply = await InvokeAsync(
            new UnavailableRenderer(),
            "apply",
            "--input",
            source,
            "--rules",
            rules,
            "--report",
            report,
            "--plan",
            planPath,
            "--approve",
            review.DirectiveId,
            "--output-dir",
            applyDirectory);

        Assert.Equal(CliExitCodes.Success, apply.ExitCode);
        using JsonDocument applyEnvelope = JsonDocument.Parse(apply.Output);
        Assert.False(
            applyEnvelope.RootElement
                .GetProperty("data")
                .GetProperty("isReadyForUse")
                .GetBoolean());
        Assert.Equal(sourceHash, Sha256(source));
        AssertSchema(
            "apply-manifest.schema.json",
            File.ReadAllText(
                Path.Combine(
                    applyDirectory,
                    "apply-manifest.json")));
        using JsonDocument changeLog = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(applyDirectory, "change-log.json")));
        string[] authorizations = changeLog.RootElement
            .GetProperty("entries")
            .EnumerateArray()
            .Select(
                item => item.GetProperty("authorization").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ["safeAutomatic", "userConfirmed"],
            authorizations);
        string exportDirectory = task.Path("export");
        Invocation export = await InvokeAsync(
            new UnavailableRenderer(),
            "export",
            "--input-dir",
            applyDirectory,
            "--output-dir",
            exportDirectory);
        Assert.Equal(CliExitCodes.InvalidInput, export.ExitCode);
        Assert.False(Directory.Exists(exportDirectory));
    }

    [Fact]
    public async Task ExperimentalAttemptIsIsolatedDiagnosticAndCannotExport()
    {
        using var task = new TestTask();
        string source = Fixture("wide-layout-risk.docx");
        string rules = task.Path("experimental-rules.json");
        string checkDirectory = task.Path("experimental-check");
        await InvokeAsync(
            new UnavailableRenderer(),
            "derive-template",
            "--ieee",
            "--output",
            rules);
        await InvokeAsync(
            new UnavailableRenderer(),
            "check",
            "--input",
            source,
            "--rules",
            rules,
            "--output-dir",
            checkDirectory);
        string report = Path.Combine(
            checkDirectory,
            "issue-report.json");
        string reportId = CheckReportJson.Deserialize(
            File.ReadAllText(report)).ReportId;
        const string experimentalId = "experimental-wide-object";
        var proposal = new AgentPlanProposal(
            reportId,
            Sha256(source),
            "codex",
            "experimental-test-agent",
            visualEvidenceUsed: true,
            externalProcessingConsent: false,
            directives: Array.Empty<ProposedDirective>(),
            layoutOperations:
            [
                new ProposedLayoutOperation(
                    experimentalId,
                    LayoutOperationKind.PreserveFullWidthObject,
                    RepairPlanDecision.Apply,
                    RepairPlanRisk.High,
                    "Keep the wide object isolated from ordinary apply.",
                    ObjectElementId:
                        "element:main/section[0]/paragraph[1]",
                    Strategy: "diagnosticOnly"),
            ]);
        string proposalPath = task.Path("experimental-proposal.json");
        File.WriteAllText(
            proposalPath,
            AgentPlanProposalJson.Serialize(proposal));
        string planPath = task.Path("experimental-plan.json");
        Invocation validation = await InvokeAsync(
            new UnavailableRenderer(),
            "plan-validate",
            "--source",
            source,
            "--report",
            report,
            "--rules",
            rules,
            "--proposal",
            proposalPath,
            "--output",
            planPath);

        Assert.Equal(CliExitCodes.NeedsConfirmation, validation.ExitCode);
        LayoutOperation experimental = Assert.Single(
            RepairPlanJson.Deserialize(
                    File.ReadAllText(planPath))
                .LayoutOperations);
        Assert.Equal(ModificationLevel.Experimental, experimental.Level);
        Assert.Equal(RepairPlanDecision.Preserve, experimental.Decision);

        string invalidAttemptDirectory = task.Path(
            "invalid-experimental-attempt");
        Invocation invalid = await InvokeAsync(
            new UnavailableRenderer(),
            "attempt-init",
            "--input",
            source,
            "--plan",
            planPath,
            "--attempt-id",
            "attempt-invalid",
            "--select-experimental",
            "unknown-operation",
            "--output-dir",
            invalidAttemptDirectory);
        Assert.Equal(CliExitCodes.InvalidInput, invalid.ExitCode);
        Assert.False(Directory.Exists(invalidAttemptDirectory));

        string attemptDirectory = task.Path("experimental-attempt");
        Invocation initialized = await InvokeAsync(
            new UnavailableRenderer(),
            "attempt-init",
            "--input",
            source,
            "--plan",
            planPath,
            "--attempt-id",
            "attempt-01",
            "--select-experimental",
            experimentalId,
            "--output-dir",
            attemptDirectory);

        Assert.Equal(
            CliExitCodes.NeedsConfirmation,
            initialized.ExitCode);
        string manifestPath = Path.Combine(
            attemptDirectory,
            "experimental-attempt.json");
        AssertSchema(
            "experimental-attempt.schema.json",
            File.ReadAllText(manifestPath));
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(manifestPath));
        Assert.Equal(
            "isolatedDiagnostic",
            manifest.RootElement.GetProperty("status").GetString());
        Assert.False(
            manifest.RootElement.GetProperty("readyForUse").GetBoolean());
        Assert.Equal(
            Sha256(source),
            Sha256(Path.Combine(attemptDirectory, "original.docx")));
        Assert.Equal(
            Sha256(source),
            Sha256(Path.Combine(attemptDirectory, "candidate.docx")));
        Assert.False(
            File.Exists(Path.Combine(attemptDirectory, "formatted.docx")));

        string exportDirectory = task.Path("experimental-export");
        Invocation export = await InvokeAsync(
            new UnavailableRenderer(),
            "export",
            "--input-dir",
            attemptDirectory,
            "--output-dir",
            exportDirectory);
        Assert.Equal(CliExitCodes.InvalidInput, export.ExitCode);
        Assert.Contains("diagnostic-only", export.Error);
        Assert.False(
            File.Exists(
                Path.Combine(exportDirectory, "export-manifest.json")));
    }

    [Fact]
    public async Task ApprovedIeeeLayoutPlanKeepsFrontMatterFullWidthAndBodyTwoColumn()
    {
        using var task = new TestTask();
        string source = Fixture("single-column-ieee-like.docx");
        string sourceHash = Sha256(source);
        ParagraphFormatting[] sourceParagraphFormatting =
            WordDocumentParser.Parse(source)
                .Document!
                .Sections
                .SelectMany(item => item.Paragraphs)
                .Select(item => item.EffectiveFormatting)
                .ToArray();
        string rules = task.Path("rules.json");
        string layoutAnalysis = task.Path("layout-analysis.json");
        await InvokeAsync(
            new UnavailableRenderer(),
            "derive-template",
            "--ieee",
            "--output",
            rules);
        Invocation analyze = await InvokeAsync(
            new UnavailableRenderer(),
            "layout-analyze",
            "--input",
            source,
            "--rules",
            rules,
            "--output",
            layoutAnalysis);

        Assert.Equal(CliExitCodes.NeedsConfirmation, analyze.ExitCode);
        AssertSchema(
            "layout-analysis.schema.json",
            File.ReadAllText(layoutAnalysis));
        using JsonDocument analysis = JsonDocument.Parse(
            File.ReadAllText(layoutAnalysis));
        Assert.True(
            analysis.RootElement.GetProperty("canConvert").GetBoolean());
        string boundary = analysis.RootElement
            .GetProperty("frontMatterEndElementId")
            .GetString()!;
        Assert.Contains(
            analysis.RootElement.GetProperty("risks").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == "equation");
        string wideAnalysisPath = task.Path("wide-layout-analysis.json");
        Invocation wideAnalyze = await InvokeAsync(
            new UnavailableRenderer(),
            "layout-analyze",
            "--input",
            Fixture("wide-layout-risk.docx"),
            "--rules",
            rules,
            "--output",
            wideAnalysisPath);
        Assert.Equal(CliExitCodes.NeedsConfirmation, wideAnalyze.ExitCode);
        using JsonDocument wideAnalysis = JsonDocument.Parse(
            File.ReadAllText(wideAnalysisPath));
        string[] riskKinds = wideAnalysis.RootElement
            .GetProperty("risks")
            .EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Contains("wideTable", riskKinds);
        Assert.Contains("inlineDrawing", riskKinds);
        Assert.Contains("equation", riskKinds);
        Assert.Contains("field", riskKinds);

        string checkDirectory = task.Path("check");
        await InvokeAsync(
            new UnavailableRenderer(),
            "check",
            "--input",
            source,
            "--rules",
            rules,
            "--output-dir",
            checkDirectory);
        string report = Path.Combine(
            checkDirectory,
            "issue-report.json");
        string reportId = CheckReportJson.Deserialize(
            File.ReadAllText(report)).ReportId;
        const string breakId = "layout-insert-body-section";
        const string columnsId = "layout-set-body-columns";
        var proposal = new AgentPlanProposal(
            reportId,
            Sha256(source),
            "codex",
            "layout-test-agent",
            visualEvidenceUsed: true,
            externalProcessingConsent: false,
            directives: Array.Empty<ProposedDirective>(),
            layoutOperations:
            [
                new ProposedLayoutOperation(
                    breakId,
                    LayoutOperationKind.InsertContinuousSectionBreak,
                    RepairPlanDecision.Apply,
                    RepairPlanRisk.Medium,
                    "Keep front matter full width.",
                    AfterElementId: boundary,
                    RollbackStrategy: "restoreSectionSnapshot"),
                new ProposedLayoutOperation(
                    columnsId,
                    LayoutOperationKind.SetSectionColumns,
                    RepairPlanDecision.Apply,
                    RepairPlanRisk.Medium,
                    "Set the body to the reviewed IEEE column geometry.",
                    DependsOnOperationIds: [breakId],
                    RollbackStrategy: "restoreSectionSnapshot",
                    TargetSectionIndex: 1,
                    ColumnCount: 2,
                    ColumnSpacingTwips: 360),
            ]);
        string proposalPath = task.Path("layout-proposal.json");
        File.WriteAllText(
            proposalPath,
            AgentPlanProposalJson.Serialize(proposal));
        string planPath = task.Path("layout-plan.json");
        Invocation validate = await InvokeAsync(
            new UnavailableRenderer(),
            "plan-validate",
            "--source",
            source,
            "--report",
            report,
            "--rules",
            rules,
            "--proposal",
            proposalPath,
            "--output",
            planPath);

        Assert.Equal(CliExitCodes.NeedsConfirmation, validate.ExitCode);
        RepairPlan plan = RepairPlanJson.Deserialize(
            File.ReadAllText(planPath));
        Assert.Equal([breakId, columnsId], plan.LayoutExecutionOrder);
        Assert.All(
            plan.LayoutOperations,
            item => Assert.Equal(
                ModificationLevel.Review,
                item.Level));

        string applyDirectory = task.Path("apply");
        Invocation apply = await InvokeAsync(
            new UnavailableRenderer(),
            "apply",
            "--input",
            source,
            "--rules",
            rules,
            "--report",
            report,
            "--plan",
            planPath,
            "--approve",
            $"{breakId},{columnsId}",
            "--output-dir",
            applyDirectory);

        Assert.Equal(CliExitCodes.Success, apply.ExitCode);
        Assert.Equal(sourceHash, Sha256(source));
        string formatted = Path.Combine(
            applyDirectory,
            "formatted.docx");
        DocumentModel document = WordDocumentParser.Parse(formatted)
            .Document!;
        Assert.Equal(2, document.Sections.Count);
        Assert.Equal(
            1,
            document.Sections[0].PageSettings.Columns.Count);
        Assert.Equal(
            2,
            document.Sections[1].PageSettings.Columns.Count);
        Assert.Equal(
            360,
            document.Sections[1].PageSettings.Columns.Spacing?.Value);
        Assert.Equal(
            sourceParagraphFormatting,
            document.Sections
                .SelectMany(item => item.Paragraphs)
                .Select(item => item.EffectiveFormatting)
                .ToArray());
        AssertSchema(
            "layout-change-log.schema.json",
            File.ReadAllText(
                Path.Combine(
                    applyDirectory,
                    "layout-change-log.json")));
        using JsonDocument integrity = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    applyDirectory,
                    "integrity-report.json")));
        Assert.Equal(
            "passed",
            integrity.RootElement.GetProperty("status").GetString());
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    applyDirectory,
                    "apply-manifest.json")));
        Assert.True(
            manifest.RootElement
                .GetProperty("readyForVisualValidation")
                .GetBoolean());
        CheckReport postCheck = CheckReportJson.Deserialize(
            File.ReadAllText(
                Path.Combine(
                    applyDirectory,
                    "post-check.json")));
        Assert.Equal(0, postCheck.Summary.ErrorCount);
        Assert.DoesNotContain(
            postCheck.Issues,
            item => item.RuleId is
                "ieee-like-v1.page.column-count"
                or "ieee-like-v1.page.column-spacing");

        var renderer = new FakeRenderer();
        string beforeRender = task.Path("before-render");
        string afterRender = task.Path("after-render");
        Invocation renderBefore = await InvokeAsync(
            renderer,
            "render",
            "--input",
            source,
            "--output-dir",
            beforeRender);
        Invocation renderAfter = await InvokeAsync(
            renderer,
            "render",
            "--input",
            formatted,
            "--output-dir",
            afterRender);
        string comparisonPath = Path.Combine(
            applyDirectory,
            "page-comparison.json");
        Invocation compare = await InvokeAsync(
            renderer,
            "compare-pages",
            "--before",
            beforeRender,
            "--after",
            afterRender,
            "--output",
            comparisonPath);
        Assert.Equal(CliExitCodes.Success, renderBefore.ExitCode);
        Assert.Equal(CliExitCodes.Success, renderAfter.ExitCode);
        Assert.Equal(CliExitCodes.Success, compare.ExitCode);
        AssertSchema(
            "page-comparison.schema.json",
            File.ReadAllText(comparisonPath));

        string planId = manifest.RootElement
            .GetProperty("planId")
            .GetString()!;
        string operationId = manifest.RootElement
            .GetProperty("operationId")
            .GetString()!;
        string submissionPath = task.Path("visual-submission.json");
        string submission = JsonSerializer.Serialize(
            new
            {
                schemaVersion = "1.0",
                planId,
                operationId,
                status = "passed",
                providerId = "codex",
                model = "test-agent",
                sourcePageCount = 1,
                outputPageCount = 1,
                findings = Array.Empty<object>(),
                summary = "All rendered pages were reviewed.",
            });
        File.WriteAllText(submissionPath, submission);
        AssertSchema(
            "agent-visual-review-submission.schema.json",
            submission);
        string visualPath = Path.Combine(
            applyDirectory,
            "validated-visual-review.json");
        Invocation visualReview = await InvokeAsync(
            renderer,
            "visual-review",
            "--apply-manifest",
            Path.Combine(applyDirectory, "apply-manifest.json"),
            "--before-render",
            beforeRender,
            "--after-render",
            afterRender,
            "--comparison",
            comparisonPath,
            "--submission",
            submissionPath,
            "--output",
            visualPath);
        Assert.Equal(CliExitCodes.Success, visualReview.ExitCode);
        AssertSchema(
            "validated-visual-review.schema.json",
            File.ReadAllText(visualPath));

        string staleSubmissionPath = task.Path(
            "stale-visual-submission.json");
        File.WriteAllText(
            staleSubmissionPath,
            submission.Replace(
                planId,
                "stale-plan",
                StringComparison.Ordinal));
        Invocation staleReview = await InvokeAsync(
            renderer,
            "visual-review",
            "--apply-manifest",
            Path.Combine(applyDirectory, "apply-manifest.json"),
            "--before-render",
            beforeRender,
            "--after-render",
            afterRender,
            "--comparison",
            comparisonPath,
            "--submission",
            staleSubmissionPath,
            "--output",
            task.Path("stale-visual-review.json"));
        Assert.Equal(CliExitCodes.InvalidInput, staleReview.ExitCode);

        string validationPath = Path.Combine(
            applyDirectory,
            "validation-report.json");
        Invocation validateOutput = await InvokeAsync(
            renderer,
            "validate-output",
            "--input-dir",
            applyDirectory,
            "--comparison",
            comparisonPath,
            "--visual-review",
            visualPath,
            "--output",
            validationPath);
        Assert.Equal(CliExitCodes.Success, validateOutput.ExitCode);
        AssertSchema(
            "validation-report.schema.json",
            File.ReadAllText(validationPath));

        string exportDirectory = task.Path("ready-export");
        Invocation export = await InvokeAsync(
            renderer,
            "export",
            "--input-dir",
            applyDirectory,
            "--output-dir",
            exportDirectory);
        Assert.Equal(CliExitCodes.Success, export.ExitCode);
        string exportManifest = File.ReadAllText(
            Path.Combine(exportDirectory, "export-manifest.json"));
        AssertSchema("export-manifest.schema.json", exportManifest);
        using JsonDocument exported = JsonDocument.Parse(exportManifest);
        Assert.Equal(
            "ready",
            exported.RootElement.GetProperty("status").GetString());
        Assert.Empty(
            exported.RootElement
                .GetProperty("remainingGates")
                .EnumerateArray());
    }

    [Fact]
    public void ReviewedNextPageSectionBreakUsesTheSameTransactionalGates()
    {
        using var task = new TestTask();
        string source = Fixture("single-column-ieee-like.docx");
        string candidate = task.Path("candidate.docx");
        File.Copy(source, candidate);
        LayoutOperation[] operations =
        [
            new LayoutOperation(
                "layout-next-page",
                LayoutOperationKind.InsertNextPageSectionBreak,
                RepairPlanDecision.Apply,
                RepairPlanRisk.Medium,
                ModificationLevel.Review,
                requiresUserConfirmation: true,
                "Start a reviewed appendix-like section on a new page.",
                "restoreSectionSnapshot",
                afterElementId:
                    "element:main/section[0]/paragraph[4]"),
            new LayoutOperation(
                "layout-next-page-columns",
                LayoutOperationKind.SetSectionColumns,
                RepairPlanDecision.Apply,
                RepairPlanRisk.Medium,
                ModificationLevel.Review,
                requiresUserConfirmation: true,
                "Apply the reviewed section column geometry.",
                "restoreSectionSnapshot",
                dependsOnOperationIds: ["layout-next-page"],
                targetSectionIndex: 1,
                columnCount: 2,
                columnSpacingTwips: 360),
        ];

        LayoutExecutionResult result =
            IeeeLayoutConverter.Apply(candidate, operations);

        Assert.True(result.PackageValid);
        Assert.True(result.Reopened);
        Assert.Contains(
            result.ChangeLog.Entries,
            item => item.Kind
                == LayoutOperationKind.InsertNextPageSectionBreak);
        DocumentModel document = WordDocumentParser.Parse(candidate)
            .Document!;
        Assert.Equal(2, document.Sections.Count);
        IntegrityReport integrity = ContentIntegrityValidator.Compare(
            source,
            candidate,
            [IntegrityCheckIds.SectionTopology]);
        Assert.Equal(IntegrityStatus.Passed, integrity.Status);
    }

    [Fact]
    public async Task InvalidArgumentsReturnStableNonZeroExitCode()
    {
        Invocation result = await InvokeAsync(
            new UnavailableRenderer(),
            "inspect");

        Assert.Equal(CliExitCodes.InvalidInput, result.ExitCode);
        Assert.Empty(result.Output);
        AssertSchema("cli-result.schema.json", result.Error);
    }

    private static async Task<Invocation> InvokeAsync(
        IDocumentRenderer renderer,
        params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = await new CliApplication(renderer).RunAsync(
            args,
            output,
            error);
        return new Invocation(
            exitCode,
            output.ToString(),
            error.ToString());
    }

    private static string Fixture(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
    }

    private static void AssertSchema(string schemaName, string json)
    {
        JsonSchema schema = SchemaCache.GetOrAdd(
            schemaName,
            name => JsonSchema.FromText(
                File.ReadAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Schemas",
                        name))));
        using JsonDocument instance = JsonDocument.Parse(json);
        EvaluationResults result = schema.Evaluate(
            instance.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
            });
        Assert.True(result.IsValid, schemaName);
    }

    private sealed record Invocation(
        int ExitCode,
        string Output,
        string Error);

    private sealed class UnavailableRenderer : IDocumentRenderer
    {
        public bool IsAvailable => false;

        public Task<RenderedDocument> RenderAsync(
            string inputPath,
            string outputDirectory,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Renderer is unavailable.");
    }

    private sealed class FakeRenderer : IDocumentRenderer
    {
        internal static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwC" +
            "AAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        public bool IsAvailable => true;

        public Task<RenderedDocument> RenderAsync(
            string inputPath,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(outputDirectory);
            string pdf = Path.Combine(outputDirectory, "document.pdf");
            string png = Path.Combine(outputDirectory, "page-1.png");
            File.WriteAllText(pdf, "%PDF-1.4");
            File.WriteAllBytes(png, Png);
            return Task.FromResult(
                new RenderedDocument(
                    pdf,
                    [new RenderedPage(1, png)]));
        }
    }

    private sealed class TestTask : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "paperformat-cli-tests",
            Guid.NewGuid().ToString("N"));

        public TestTask()
        {
            Directory.CreateDirectory(_root);
        }

        public string Path(string relative) =>
            System.IO.Path.Combine(_root, relative);

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
