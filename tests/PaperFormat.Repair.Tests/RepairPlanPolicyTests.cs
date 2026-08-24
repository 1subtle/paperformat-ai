using PaperFormat.Ai;
using PaperFormat.Checking;
using PaperFormat.Classification;
using PaperFormat.Domain;
using PaperFormat.OpenXml;
using PaperFormat.Rules;

namespace PaperFormat.Repair.Tests;

public sealed class RepairPlanPolicyTests
{
    private const string SourceSha256 =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void ParagraphStyleApplyIsBlockedToProtectNumberingAndLayout()
    {
        RulePackage rules = Rules();
        FormatRule rule = Rule(
            rules,
            RuleTarget.Body,
            FormatProperty.ParagraphStyleId);
        CheckReport report = ReportFor(rules, rule);

        ProposedDirective proposal = ProposalFor(
            rules,
            report,
            rule,
            RepairPlanDecision.Apply,
            RepairPlanRisk.Low,
            0.99m,
            "Replace the body style.");
        RepairPlan plan = Plan(rules, report, proposal);

        RepairPlanDirective directive = Directive(plan, proposal.GroupId);
        Assert.Equal(RepairPlanDecision.Preserve, directive.Decision);
        Assert.Equal(RepairPlanRisk.Blocked, directive.Risk);
        Assert.Contains(
            directive.SafetyNotes,
            note => note.Contains("numbering", StringComparison.Ordinal));
    }

    [Fact]
    public void TableParagraphLayoutApplyIsBlockedButCharacterFormattingMayPass()
    {
        RulePackage rules = Rules();
        FormatRule alignment = Rule(
            rules,
            RuleTarget.TableText,
            FormatProperty.ParagraphAlignment);
        FormatRule font = Rule(
            rules,
            RuleTarget.TableText,
            FormatProperty.FontAscii);
        CheckReport report = ReportFor(rules, alignment, font);

        ProposedDirective alignmentProposal = ProposalFor(
            rules,
            report,
            alignment,
            RepairPlanDecision.Apply,
            RepairPlanRisk.Low,
            0.99m,
            "Normalize cell alignment.");
        ProposedDirective fontProposal = ProposalFor(
            rules,
            report,
            font,
            RepairPlanDecision.Apply,
            RepairPlanRisk.Low,
            0.99m,
            "Normalize table font without changing geometry.");
        RepairPlan plan = RepairPlanPolicy.Validate(
            report,
            rules,
            [
                alignmentProposal,
                fontProposal,
            ],
            "test-provider",
            "test-model",
            RepairPlanOrigin.OpenAi,
            visualEvidenceUsed: true,
            externalProcessingConsent: true,
            sourceSha256: SourceSha256);

        Assert.Equal(
            RepairPlanDecision.Preserve,
            Directive(plan, alignmentProposal.GroupId).Decision);
        Assert.Equal(
            RepairPlanDecision.Apply,
            Directive(plan, fontProposal.GroupId).Decision);
    }

    [Fact]
    public void TableCharacterRepairIsScopedToOneTable()
    {
        RulePackage rules = Rules();
        FormatRule font = Rule(
            rules,
            RuleTarget.TableText,
            FormatProperty.FontAscii);
        CheckReport report = ReportForLocations(
            rules,
            font,
            new StructuralLocation(
                DocumentPartKind.MainDocument,
                sectionIndex: 0,
                tableIndex: 0,
                rowIndex: 0,
                cellIndex: 0,
                paragraphIndex: 0,
                runIndex: 0),
            new StructuralLocation(
                DocumentPartKind.MainDocument,
                sectionIndex: 0,
                tableIndex: 1,
                rowIndex: 0,
                cellIndex: 0,
                paragraphIndex: 0,
                runIndex: 0));
        RepairPlanCandidateGroup[] groups =
            RepairPlanPolicy.CreateCandidateGroups(report, rules)
                .Where(item => item.RuleId == font.RuleId)
                .ToArray();
        Assert.Equal(2, groups.Length);
        RepairPlan plan = RepairPlanPolicy.Validate(
            report,
            rules,
            [
                new ProposedDirective(
                    groups[0].GroupId,
                    RepairPlanDecision.Apply,
                    RepairPlanRisk.Low,
                    0.99m,
                    "The first ordinary table needs the confirmed font."),
                new ProposedDirective(
                    groups[1].GroupId,
                    RepairPlanDecision.Preserve,
                    RepairPlanRisk.High,
                    0.99m,
                    "Preserve the Algorithm table as a special layout."),
            ],
            "test-provider",
            "test-model",
            RepairPlanOrigin.OpenAi,
            visualEvidenceUsed: true,
            externalProcessingConsent: true,
            sourceSha256: SourceSha256);
        RepairPlanDirective selected = Directive(
            plan,
            groups[0].GroupId);

        string[] issueIds = RepairPlanPolicy.ExpandApprovedIssues(
            plan,
            report,
            [selected.DirectiveId]);

        Assert.Equal(groups[0].IssueIds, issueIds);
        Assert.DoesNotContain(groups[1].IssueIds[0], issueIds);
    }

    [Fact]
    public void LowConfidenceIndentProposalIsDowngradedAndCannotExecute()
    {
        (RulePackage rules, CheckReport report) = CheckWrongFixture();
        FormatRule indent = Rule(
            rules,
            RuleTarget.Body,
            FormatProperty.FirstLineIndent);

        ProposedDirective proposal = ProposalFor(
            rules,
            report,
            indent,
            RepairPlanDecision.Apply,
            RepairPlanRisk.Low,
            0.79m,
            "Indentation evidence is ambiguous.");
        RepairPlan plan = Plan(rules, report, proposal);

        RepairPlanDirective directive = Directive(plan, proposal.GroupId);
        Assert.Equal(RepairPlanDecision.ReportOnly, directive.Decision);
        Assert.Equal(RepairPlanRisk.High, directive.Risk);
        Assert.Empty(
            RepairPlanPolicy.ExpandApprovedIssues(
                plan,
                report,
                [directive.DirectiveId]));
    }

    [Fact]
    public void LowRiskParagraphFormattingIsSafeWithoutConfirmation()
    {
        (RulePackage rules, CheckReport report) = CheckWrongFixture();
        FormatRule indent = Rule(
            rules,
            RuleTarget.Body,
            FormatProperty.FirstLineIndent);
        ProposedDirective proposal = ProposalFor(
            rules,
            report,
            indent,
            RepairPlanDecision.Apply,
            RepairPlanRisk.Low,
            0.98m,
            "Apply the confirmed local body indentation.");

        RepairPlanDirective directive = Directive(
            Plan(rules, report, proposal),
            proposal.GroupId);

        Assert.Equal(RepairPlanDecision.Apply, directive.Decision);
        Assert.Equal(ModificationLevel.Safe, directive.Level);
        Assert.False(directive.RequiresUserConfirmation);
    }

    [Fact]
    public void NonExecutableDecisionsAreAdvisoryInsteadOfExperimental()
    {
        RulePackage rules = Rules();
        FormatRule font = Rule(
            rules,
            RuleTarget.Body,
            FormatProperty.FontAscii);
        CheckReport report = ReportFor(rules, font);
        ProposedDirective proposal = ProposalFor(
            rules,
            report,
            font,
            RepairPlanDecision.ReportOnly,
            RepairPlanRisk.High,
            0.70m,
            "Keep this mismatch visible without changing the document.");

        RepairPlanDirective directive = Directive(
            Plan(rules, report, proposal),
            proposal.GroupId);

        Assert.Equal(ModificationLevel.Advisory, directive.Level);
        Assert.False(directive.RequiresUserConfirmation);
    }

    [Fact]
    public void ApplyWithoutRenderedEvidenceFailsClosed()
    {
        RulePackage rules = Rules();
        FormatRule rule = Rule(
            rules,
            RuleTarget.Body,
            FormatProperty.FontAscii);
        CheckReport report = ReportFor(rules, rule);
        ProposedDirective proposal = ProposalFor(
            rules,
            report,
            rule,
            RepairPlanDecision.Apply,
            RepairPlanRisk.Low,
            0.99m,
            "Apply the confirmed body font.");

        RepairPlan plan = RepairPlanPolicy.Validate(
            report,
            rules,
            [proposal],
            "codex",
            "codex-agent",
            RepairPlanOrigin.ExternalAgent,
            visualEvidenceUsed: false,
            externalProcessingConsent: true,
            sourceSha256: SourceSha256);

        RepairPlanDirective directive = Directive(plan, proposal.GroupId);
        Assert.Equal(RepairPlanDecision.ReportOnly, directive.Decision);
        Assert.Equal(RepairPlanRisk.Blocked, directive.Risk);
        Assert.Contains(
            directive.SafetyNotes,
            note => note.Contains(
                "visual evidence",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConflictingDuplicateScopeDirectivesFailClosed()
    {
        RulePackage rules = Rules();
        FormatRule rule = Rule(
            rules,
            RuleTarget.Body,
            FormatProperty.FontAscii);
        CheckReport report = ReportFor(rules, rule);
        RepairPlanCandidateGroup group = Assert.Single(
            RepairPlanPolicy.CreateCandidateGroups(report, rules));

        RepairPlan plan = RepairPlanPolicy.Validate(
            report,
            rules,
            [
                new ProposedDirective(
                    group.GroupId,
                    RepairPlanDecision.Apply,
                    RepairPlanRisk.Low,
                    0.99m,
                    "Apply the font."),
                new ProposedDirective(
                    group.GroupId,
                    RepairPlanDecision.Preserve,
                    RepairPlanRisk.High,
                    0.99m,
                    "Preserve the font."),
            ],
            "test-provider",
            "test-model",
            RepairPlanOrigin.OpenAi,
            visualEvidenceUsed: true,
            externalProcessingConsent: true,
            sourceSha256: SourceSha256);

        Assert.Equal(
            RepairPlanDecision.ReportOnly,
            Assert.Single(plan.Directives).Decision);
        Assert.Contains(
            plan.Notices,
            notice => notice.Contains(
                "duplicate",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApprovedHighConfidenceIndentDirectiveExpandsOnlyItsIssues()
    {
        (RulePackage rules, CheckReport report) = CheckWrongFixture();
        FormatRule indent = Rule(
            rules,
            RuleTarget.Body,
            FormatProperty.FirstLineIndent);
        ProposedDirective proposal = ProposalFor(
            rules,
            report,
            indent,
            RepairPlanDecision.Apply,
            RepairPlanRisk.Medium,
            0.98m,
            "Normal body paragraphs need the confirmed first-line indent.");
        RepairPlan plan = Plan(rules, report, proposal);
        RepairPlanDirective directive = Directive(plan, proposal.GroupId);

        string[] issueIds = RepairPlanPolicy.ExpandApprovedIssues(
            plan,
            report,
            [directive.DirectiveId]);

        Assert.NotEmpty(issueIds);
        Assert.All(
            report.Issues.Where(item => issueIds.Contains(item.IssueId)),
            item => Assert.Equal(indent.RuleId, item.RuleId));
        Assert.True(directive.RequiresUserConfirmation);
    }

    [Fact]
    public void RepairPlanJsonRoundTripsValidatedDirectives()
    {
        (RulePackage rules, CheckReport report) = CheckWrongFixture();
        FormatRule indent = Rule(
            rules,
            RuleTarget.Body,
            FormatProperty.FirstLineIndent);
        ProposedDirective proposal = ProposalFor(
            rules,
            report,
            indent,
            RepairPlanDecision.Apply,
            RepairPlanRisk.Medium,
            0.98m,
            "Indent normal body paragraphs.");
        RepairPlan original = Plan(rules, report, proposal);

        RepairPlan restored = RepairPlanJson.Deserialize(
            RepairPlanJson.Serialize(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void LayoutDependenciesAreOrderedAndIncludedInPlanIdentity()
    {
        (RulePackage rules, CheckReport report) = CheckWrongFixture();
        ProposedLayoutOperation sectionBreak = new(
            "layout-break",
            LayoutOperationKind.InsertContinuousSectionBreak,
            RepairPlanDecision.Apply,
            RepairPlanRisk.Medium,
            "Create the reviewed body boundary.",
            AfterElementId: "element:main/section[0]/paragraph[4]");
        ProposedLayoutOperation twoColumns = new(
            "layout-columns",
            LayoutOperationKind.SetSectionColumns,
            RepairPlanDecision.Apply,
            RepairPlanRisk.Medium,
            "Apply two columns after the boundary.",
            DependsOnOperationIds: ["layout-break"],
            TargetSectionIndex: 1,
            ColumnCount: 2,
            ColumnSpacingTwips: 360);

        RepairPlan plan = RepairPlanPolicy.Validate(
            report,
            rules,
            Array.Empty<ProposedDirective>(),
            "test-provider",
            "test-model",
            RepairPlanOrigin.ExternalAgent,
            visualEvidenceUsed: true,
            externalProcessingConsent: false,
            sourceSha256: SourceSha256,
            proposedLayoutOperations: [twoColumns, sectionBreak]);
        RepairPlan changed = RepairPlanPolicy.Validate(
            report,
            rules,
            Array.Empty<ProposedDirective>(),
            "test-provider",
            "test-model",
            RepairPlanOrigin.ExternalAgent,
            visualEvidenceUsed: true,
            externalProcessingConsent: false,
            sourceSha256: SourceSha256,
            proposedLayoutOperations:
            [
                sectionBreak,
                twoColumns with { ColumnCount = 3 },
            ]);

        Assert.Equal(
            ["layout-break", "layout-columns"],
            plan.LayoutExecutionOrder);
        Assert.NotEqual(plan.PlanId, changed.PlanId);
        Assert.Equal(
            plan,
            RepairPlanJson.Deserialize(
                RepairPlanJson.Serialize(plan)));
    }

    [Fact]
    public void LayoutDependencyCycleFailsClosed()
    {
        (RulePackage rules, CheckReport report) = CheckWrongFixture();

        Assert.Throws<ArgumentException>(
            () => RepairPlanPolicy.Validate(
                report,
                rules,
                Array.Empty<ProposedDirective>(),
                "test-provider",
                "test-model",
                RepairPlanOrigin.ExternalAgent,
                visualEvidenceUsed: true,
                externalProcessingConsent: false,
                sourceSha256: SourceSha256,
                proposedLayoutOperations:
                [
                    new ProposedLayoutOperation(
                        "layout-break",
                        LayoutOperationKind.InsertContinuousSectionBreak,
                        RepairPlanDecision.Apply,
                        RepairPlanRisk.Medium,
                        "Create the boundary.",
                        DependsOnOperationIds: ["layout-columns"],
                        AfterElementId:
                            "element:main/section[0]/paragraph[4]"),
                    new ProposedLayoutOperation(
                        "layout-columns",
                        LayoutOperationKind.SetSectionColumns,
                        RepairPlanDecision.Apply,
                        RepairPlanRisk.Medium,
                        "Apply columns.",
                        DependsOnOperationIds: ["layout-break"],
                        TargetSectionIndex: 1,
                        ColumnCount: 2,
                        ColumnSpacingTwips: 360),
                ]));
    }

    [Fact]
    public void FullWidthObjectOperationRemainsNonExecutableExperimental()
    {
        (RulePackage rules, CheckReport report) = CheckWrongFixture();

        RepairPlan plan = RepairPlanPolicy.Validate(
            report,
            rules,
            Array.Empty<ProposedDirective>(),
            "test-provider",
            "test-model",
            RepairPlanOrigin.ExternalAgent,
            visualEvidenceUsed: true,
            externalProcessingConsent: false,
            sourceSha256: SourceSha256,
            proposedLayoutOperations:
            [
                new ProposedLayoutOperation(
                    "layout-wide-figure",
                    LayoutOperationKind.PreserveFullWidthObject,
                    RepairPlanDecision.Apply,
                    RepairPlanRisk.Medium,
                    "The wide figure needs a temporary single-column section.",
                    ObjectElementId:
                        "element:main/section[0]/paragraph[12]",
                    Strategy: "temporarySingleColumnSection"),
            ]);

        LayoutOperation operation =
            Assert.Single(plan.LayoutOperations);
        Assert.Equal(ModificationLevel.Experimental, operation.Level);
        Assert.Equal(RepairPlanDecision.Preserve, operation.Decision);
        Assert.True(operation.RequiresUserConfirmation is false);
    }

    private static RepairPlan Plan(
        RulePackage rules,
        CheckReport report,
        ProposedDirective directive) =>
        RepairPlanPolicy.Validate(
            report,
            rules,
            [directive],
            "test-provider",
            "test-model",
            RepairPlanOrigin.OpenAi,
            visualEvidenceUsed: true,
            externalProcessingConsent: true,
            sourceSha256: SourceSha256);

    private static RepairPlanDirective Directive(
        RepairPlan plan,
        string groupId) =>
        Assert.Single(
            plan.Directives,
            item => item.ScopeId == groupId);

    private static ProposedDirective ProposalFor(
        RulePackage rules,
        CheckReport report,
        FormatRule rule,
        RepairPlanDecision decision,
        RepairPlanRisk risk,
        decimal confidence,
        string reason)
    {
        RepairPlanCandidateGroup group =
            RepairPlanPolicy.CreateCandidateGroups(report, rules)
                .First(item => item.RuleId == rule.RuleId);
        return new ProposedDirective(
            group.GroupId,
            decision,
            risk,
            confidence,
            reason);
    }

    private static FormatRule Rule(
        RulePackage rules,
        RuleTarget target,
        FormatProperty property) =>
        Assert.Single(
            rules.Rules,
            item => item.Target == target && item.Property == property);

    private static (RulePackage Rules, CheckReport Report) CheckWrongFixture()
    {
        RulePackage rules = Rules();
        DocumentModel document = Document();
        CheckReport report = new FormatCheckEngine().Check(
            document,
            rules,
            new DeterministicDocumentClassifier().Classify(document));
        return (rules, report);
    }

    private static CheckReport ReportFor(
        RulePackage rules,
        params FormatRule[] selectedRules)
    {
        CheckIssue[] issues = selectedRules
            .Select(
                (rule, index) => new CheckIssue(
                    $"synthetic-issue-{index}",
                    rule.RuleId,
                    rule.Severity,
                    rule.Target,
                    new StructuralLocation(
                        DocumentPartKind.MainDocument,
                        sectionIndex: 0,
                        paragraphIndex: index,
                        tableIndex:
                            rule.Target == RuleTarget.TableText ? 0 : null,
                        rowIndex:
                            rule.Target == RuleTarget.TableText ? 0 : null,
                        cellIndex:
                            rule.Target == RuleTarget.TableText ? 0 : null),
                    null,
                    rule.Expected,
                    "Synthetic policy issue.",
                    rule.Evidence,
                    1m,
                    autoFixable: true))
            .ToArray();
        int errors = issues.Count(
            item => item.Severity == RuleSeverity.Error);
        int warnings = issues.Count(
            item => item.Severity == RuleSeverity.Warning);
        int information = issues.Count(
            item => item.Severity == RuleSeverity.Information);
        return new CheckReport(
            "synthetic-policy-report",
            rules.PackageId,
            rules.Revision,
            CheckStatus.IssuesFound,
            new CheckSummary(
                selectedRules.Length,
                selectedRules.Length,
                0,
                issues.Length,
                errors,
                warnings,
                information,
                0,
                0,
                0),
            issues,
            Array.Empty<SkippedRule>(),
            Array.Empty<PendingElement>());
    }

    private static CheckReport ReportForLocations(
        RulePackage rules,
        FormatRule rule,
        params StructuralLocation[] locations)
    {
        CheckIssue[] issues = locations
            .Select(
                (location, index) => new CheckIssue(
                    $"scoped-issue-{index}",
                    rule.RuleId,
                    rule.Severity,
                    rule.Target,
                    location,
                    null,
                    rule.Expected,
                    "Synthetic scoped policy issue.",
                    rule.Evidence,
                    1m,
                    autoFixable: true))
            .ToArray();
        return new CheckReport(
            "synthetic-scoped-policy-report",
            rules.PackageId,
            rules.Revision,
            CheckStatus.IssuesFound,
            new CheckSummary(
                1,
                issues.Length,
                0,
                issues.Length,
                issues.Count(item => item.Severity == RuleSeverity.Error),
                issues.Count(item => item.Severity == RuleSeverity.Warning),
                issues.Count(
                    item => item.Severity == RuleSeverity.Information),
                0,
                0,
                0),
            issues,
            Array.Empty<SkippedRule>(),
            Array.Empty<PendingElement>());
    }

    private static DocumentModel Document() =>
        Assert.IsType<DocumentModel>(
            WordDocumentParser.Parse(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "wrong-format.docx"))
            .Document);

    private static RulePackage Rules() =>
        new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));

}
