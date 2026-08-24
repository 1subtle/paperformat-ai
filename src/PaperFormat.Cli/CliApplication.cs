using System.Security.Cryptography;
using PaperFormat.Ai;
using PaperFormat.Checking;
using PaperFormat.Classification;
using PaperFormat.Domain;
using PaperFormat.Integrity;
using PaperFormat.Layout;
using PaperFormat.OpenXml;
using PaperFormat.Rendering;
using PaperFormat.Reporting;
using PaperFormat.Repair;
using PaperFormat.Rules;

namespace PaperFormat.Cli;

public sealed class CliApplication
{
    private readonly IDocumentRenderer _renderer;

    public CliApplication(IDocumentRenderer renderer)
    {
        _renderer = renderer
            ?? throw new ArgumentNullException(nameof(renderer));
    }

    public async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            await output.WriteLineAsync(Help());
            return CliExitCodes.Success;
        }

        string command = args[0];
        try
        {
            Arguments options = Arguments.Parse(args[1..]);
            return command switch
            {
                "inspect" => await InspectAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "derive-template" => await DeriveTemplateAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "classify" => await ClassifyAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "layout-analyze" => await LayoutAnalyzeAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "check" => await CheckAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "plan-validate" => await PlanValidateAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "apply" => await ApplyAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "attempt-init" => await AttemptInitAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "render" => await RenderAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "compare-pages" => await ComparePagesAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "visual-review" => await VisualReviewAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "validate-integrity" => await ValidateIntegrityAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "validate-output" => await ValidateOutputAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "export" => await ExportAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                "run-workflow" => await RunWorkflowAsync(
                    command,
                    options,
                    output,
                    cancellationToken),
                _ => await InvalidAsync(
                    command,
                    $"Unknown command '{command}'.",
                    error),
            };
        }
        catch (CliInputException exception)
        {
            return await InvalidAsync(command, exception.Message, error);
        }
        catch (InvalidDataException exception)
        {
            return await FailureAsync(
                command,
                "invalid_document",
                exception.Message,
                CliExitCodes.InvalidInput,
                error);
        }
        catch (FileNotFoundException exception)
        {
            return await FailureAsync(
                command,
                "file_not_found",
                exception.Message,
                CliExitCodes.InvalidInput,
                error);
        }
        catch (DirectoryNotFoundException exception)
        {
            return await FailureAsync(
                command,
                "directory_not_found",
                exception.Message,
                CliExitCodes.InvalidInput,
                error);
        }
        catch (InvalidOperationException exception)
            when (command == "render")
        {
            return await FailureAsync(
                command,
                "renderer_unavailable",
                exception.Message,
                CliExitCodes.ToolUnavailable,
                error);
        }
        catch (Exception exception)
        {
            return await FailureAsync(
                command,
                "unexpected_failure",
                exception.Message,
                CliExitCodes.UnexpectedFailure,
                error);
        }
    }

    private static async Task<int> InspectAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string input = options.RequiredPath("input");
        string? outputPath = options.OptionalPath("output");
        DocumentParseResult parse = WordDocumentParser.Parse(input);
        DocumentModel document = RequireDocument(parse);
        DocumentInspection inspection =
            WordDocumentInspector.Inspect(input);
        var data = new
        {
            inspection,
            documentModel = document,
        };
        await WriteSuccessAsync(
            command,
            data,
            outputPath,
            output,
            token);
        return CliExitCodes.Success;
    }

    private static async Task<int> DeriveTemplateAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string outputPath = options.RequiredPath("output");
        bool ieee = options.Flag("ieee");
        string? input = options.OptionalPath("input");
        if (ieee == (input is not null))
        {
            throw new CliInputException(
                "Choose exactly one of --input TEMPLATE or --ieee.");
        }

        RulePackage rules;
        if (ieee)
        {
            rules = new BuiltInIeeeRuleProvider().Extract(
                new BuiltInFormatRequirementSource(
                    BuiltInIeeeRuleProvider.ProfileId));
        }
        else
        {
            DocumentModel template = RequireDocument(
                WordDocumentParser.Parse(input!));
            rules = new WordTemplateRuleProvider().Extract(
                new WordTemplateFormatRequirementSource(
                    Path.GetFileName(input!),
                    template));
        }

        await File.WriteAllTextAsync(
            outputPath,
            RulePackageJson.Serialize(rules) + Environment.NewLine,
            token);
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                "passed",
                new
                {
                    output = outputPath,
                    rules.PackageId,
                    rules.Revision,
                    ruleCount = rules.Rules.Count,
                    noticeCount = rules.Notices.Count,
                },
                Array.Empty<CliDiagnostic>(),
                ["Run classify and check with this rule package."]),
            output,
            token);
        return CliExitCodes.Success;
    }

    private static async Task<int> ClassifyAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string input = options.RequiredPath("input");
        string outputPath = options.RequiredPath("output");
        DocumentModel document = RequireDocument(
            WordDocumentParser.Parse(input));
        ClassificationSet classifications =
            new DeterministicDocumentClassifier().Classify(document);
        await File.WriteAllTextAsync(
            outputPath,
            ClassificationJson.Serialize(classifications)
            + Environment.NewLine,
            token);
        int pending = classifications.Elements.Count(
            item => item.Status != ClassificationStatus.Confirmed);
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                "passed",
                new
                {
                    output = outputPath,
                    classifications.Revision,
                    elementCount = classifications.Elements.Count,
                    advisoryCount = pending,
                },
                Array.Empty<CliDiagnostic>(),
                pending == 0
                    ? ["Run check with the classification artifact."]
                    : ["Continue with format checking; review advisory classifications only before a structural operation depends on them."]),
            output,
            token);
        return CliExitCodes.Success;
    }

    private static async Task<int> LayoutAnalyzeAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string input = options.RequiredPath("input");
        string rulesPath = options.RequiredPath("rules");
        string outputPath = options.RequiredPath("output");
        DocumentModel document = RequireDocument(
            WordDocumentParser.Parse(input));
        RulePackage rules = RulePackageJson.Deserialize(
            await File.ReadAllTextAsync(rulesPath, token));
        ClassificationSet classifications =
            await ClassificationsAsync(options, document, token);
        LayoutAnalysis analysis = IeeeLayoutAnalyzer.Analyze(
            input,
            rules,
            classifications);
        await CliJson.WriteFileAsync(outputPath, analysis, token);
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                analysis.CanConvert
                    ? analysis.Risks.Count > 0
                        ? "needsConfirmation"
                        : "passed"
                    : "failed",
                new
                {
                    output = outputPath,
                    analysis.SourceSectionCount,
                    sourceColumnCounts = analysis.SourceColumnCounts,
                    analysis.TargetColumnCount,
                    analysis.TargetColumnSpacingTwips,
                    analysis.FrontMatterEndElementId,
                    analysis.BodyStartElementId,
                    analysis.CanConvert,
                    blockerCount = analysis.Blockers.Count,
                    riskCount = analysis.Risks.Count,
                },
                Array.Empty<CliDiagnostic>(),
                analysis.CanConvert
                    ? ["Use the exact boundary and risk inventory to propose Review layout operations."]
                    : ["Resolve layout-analysis blockers or use an isolated Experimental workflow."]),
            output,
            token);
        return !analysis.CanConvert
            ? CliExitCodes.ValidationFailed
            : analysis.Risks.Count > 0
                ? CliExitCodes.NeedsConfirmation
                : CliExitCodes.Success;
    }

    private static async Task<int> CheckAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string input = options.RequiredPath("input");
        string rulesPath = options.RequiredPath("rules");
        string outputDirectory = options.RequiredPath("output-dir");
        EnsureNewDirectory(outputDirectory);
        DocumentModel document = RequireDocument(
            WordDocumentParser.Parse(input));
        RulePackage rules = RulePackageJson.Deserialize(
            await File.ReadAllTextAsync(rulesPath, token));
        ClassificationSet classifications =
            await ClassificationsAsync(options, document, token);
        CheckReport report = new FormatCheckEngine().Check(
            document,
            rules,
            classifications);
        string jsonPath = Path.Combine(
            outputDirectory,
            "issue-report.json");
        string htmlPath = Path.Combine(
            outputDirectory,
            "issue-report.html");
        string candidatesPath = Path.Combine(
            outputDirectory,
            "plan-candidates.json");
        await File.WriteAllTextAsync(
            jsonPath,
            CheckReportJson.Serialize(report) + Environment.NewLine,
            token);
        await File.WriteAllTextAsync(
            htmlPath,
            CheckReportHtml.Render(report),
            token);
        await CliJson.WriteFileAsync(
            candidatesPath,
            new
            {
                schemaVersion = AgentPlanProposal.CurrentSchemaVersion,
                sourceReportId = report.ReportId,
                sourceSha256 = Hash(input),
                rulePackageId = rules.PackageId,
                rulePackageRevision = rules.Revision,
                candidateScopes =
                    RepairPlanPolicy.CreateCandidateGroups(report, rules),
            },
            token);
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                Status(report.Status),
                new
                {
                    json = jsonPath,
                    html = htmlPath,
                    candidates = candidatesPath,
                    report.ReportId,
                    report.Summary.Score,
                    report.Summary.IssueCount,
                    report.Summary.ErrorCount,
                    report.Summary.WarningCount,
                    report.Summary.PendingElementCount,
                },
                Array.Empty<CliDiagnostic>(),
                report.Status == CheckStatus.Passed
                    ? ["Proceed to render and validation."]
                    : ["Let the Agent build a typed plan from exact issue identifiers."]),
            output,
            token);
        return report.Status == CheckStatus.NeedsConfirmation
            ? CliExitCodes.NeedsConfirmation
            : CliExitCodes.Success;
    }

    private static async Task<int> PlanValidateAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string reportPath = options.RequiredPath("report");
        string rulesPath = options.RequiredPath("rules");
        string proposalPath = options.RequiredPath("proposal");
        string sourcePath = options.RequiredPath("source");
        string outputPath = options.RequiredPath("output");
        CheckReport report = CheckReportJson.Deserialize(
            await File.ReadAllTextAsync(reportPath, token));
        RulePackage rules = RulePackageJson.Deserialize(
            await File.ReadAllTextAsync(rulesPath, token));
        AgentPlanProposal proposal = AgentPlanProposalJson.Deserialize(
            await File.ReadAllTextAsync(proposalPath, token));
        if (!string.Equals(
                proposal.SchemaVersion,
                AgentPlanProposal.CurrentSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new CliInputException(
                $"Agent proposal schema must be '{AgentPlanProposal.CurrentSchemaVersion}'.");
        }

        if (!string.Equals(
                proposal.SourceReportId,
                report.ReportId,
                StringComparison.Ordinal))
        {
            throw new CliInputException(
                "The Agent proposal does not belong to this check report.");
        }

        if (!string.Equals(
                proposal.SourceSha256,
                Hash(sourcePath),
                StringComparison.Ordinal))
        {
            throw new CliInputException(
                "The Agent proposal is bound to a different source DOCX.");
        }

        RepairPlan plan = RepairPlanPolicy.Validate(
            report,
            rules,
            proposal.Directives,
            proposal.ProviderId,
            proposal.Model,
            RepairPlanOrigin.ExternalAgent,
            proposal.VisualEvidenceUsed,
            proposal.ExternalProcessingConsent,
            proposal.SourceSha256,
            proposal.LayoutOperations);
        await File.WriteAllTextAsync(
            outputPath,
            RepairPlanJson.Serialize(plan) + Environment.NewLine,
            token);
        int advisory = plan.Directives.Count(
            item => item.Level == ModificationLevel.Advisory);
        int safe = plan.Directives.Count(
            item => item.Decision == RepairPlanDecision.Apply
                && item.Level == ModificationLevel.Safe);
        int review = plan.Directives.Count(
            item => item.Decision == RepairPlanDecision.Apply
                && item.Level == ModificationLevel.Review);
        int experimental = plan.Directives.Count(
            item => item.Level == ModificationLevel.Experimental);
        int layoutReview = plan.LayoutOperations.Count(
            item => item.Decision == RepairPlanDecision.Apply
                && item.Level == ModificationLevel.Review);
        int layoutExperimental = plan.LayoutOperations.Count(
            item => item.Level == ModificationLevel.Experimental);
        bool needsConfirmation = review + layoutReview
            + layoutExperimental > 0;
        string status = needsConfirmation
            ? "needsConfirmation"
            : "passed";
        var nextActions = new List<string>();
        if (review + layoutReview > 0)
        {
            nextActions.Add(
                "Ask the user to approve exact Review directive IDs before apply.");
        }

        if (experimental + layoutExperimental > 0)
        {
            nextActions.Add(
                "Experimental items are optional diagnostics and do not block ordinary Safe apply; initialize an isolated attempt only for exact selected IDs.");
        }

        if (nextActions.Count == 0)
        {
            nextActions.Add("Apply the validated Safe directives.");
        }

        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                status,
                new
                {
                    output = outputPath,
                    plan.PlanId,
                    advisoryDirectiveCount = advisory,
                    safeDirectiveCount = safe,
                    reviewDirectiveCount = review,
                    experimentalDirectiveCount = experimental,
                    reviewLayoutOperationCount = layoutReview,
                    experimentalLayoutOperationCount = layoutExperimental,
                    noticeCount = plan.Notices.Count,
                },
                Array.Empty<CliDiagnostic>(),
                nextActions),
            output,
            token);
        return needsConfirmation
            ? CliExitCodes.NeedsConfirmation
            : CliExitCodes.Success;
    }

    private static async Task<int> ApplyAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string input = options.RequiredPath("input");
        string rulesPath = options.RequiredPath("rules");
        string reportPath = options.RequiredPath("report");
        string planPath = options.RequiredPath("plan");
        string outputDirectory = options.RequiredPath("output-dir");
        EnsureNewDirectory(outputDirectory);
        RulePackage rules = RulePackageJson.Deserialize(
            await File.ReadAllTextAsync(rulesPath, token));
        CheckReport report = CheckReportJson.Deserialize(
            await File.ReadAllTextAsync(reportPath, token));
        RepairPlan plan = RepairPlanJson.Deserialize(
            await File.ReadAllTextAsync(planPath, token));
        if (!string.Equals(
                plan.SourceReportId,
                report.ReportId,
                StringComparison.Ordinal))
        {
            throw new CliInputException(
                "The validated plan does not belong to this check report.");
        }

        if (!string.Equals(
                plan.SourceSha256,
                Hash(input),
                StringComparison.Ordinal))
        {
            throw new CliInputException(
                "The validated plan is bound to a different source DOCX.");
        }

        HashSet<string> requestedReview = options
            .OptionalValue("approve")
            ?.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal)
            ?? [];
        RepairPlanDirective[] safe = plan.Directives
            .Where(
                item => item.Decision == RepairPlanDecision.Apply
                    && item.Level == ModificationLevel.Safe)
            .ToArray();
        RepairPlanDirective[] review = plan.Directives
            .Where(
                item => item.Decision == RepairPlanDecision.Apply
                    && item.Level == ModificationLevel.Review)
            .ToArray();
        HashSet<string> reviewIds = review
            .Select(item => item.DirectiveId)
            .ToHashSet(StringComparer.Ordinal);
        LayoutOperation[] layoutReview = plan.LayoutOperations
            .Where(
                item => item.Decision == RepairPlanDecision.Apply
                    && item.Level == ModificationLevel.Review)
            .ToArray();
        HashSet<string> layoutReviewIds = layoutReview
            .Select(item => item.OperationId)
            .ToHashSet(StringComparer.Ordinal);
        if (options.Flag("approve-all-review"))
        {
            requestedReview.UnionWith(reviewIds);
            requestedReview.UnionWith(layoutReviewIds);
        }

        string? invalidApproval = requestedReview.FirstOrDefault(
            id => !reviewIds.Contains(id)
                && !layoutReviewIds.Contains(id));
        if (invalidApproval is not null)
        {
            throw new CliInputException(
                $"Directive '{invalidApproval}' is not an executable Review directive.");
        }

        RepairPlanDirective[] approvedReview = review
            .Where(item => requestedReview.Contains(item.DirectiveId))
            .ToArray();
        Dictionary<string, LayoutOperation> layoutById =
            plan.LayoutOperations.ToDictionary(
                item => item.OperationId,
                StringComparer.Ordinal);
        LayoutOperation[] approvedLayout = plan.LayoutExecutionOrder
            .Where(requestedReview.Contains)
            .Select(id => layoutById[id])
            .ToArray();
        string? missingDependency = approvedLayout
            .SelectMany(
                item => item.DependsOnOperationIds
                    .Where(id => !requestedReview.Contains(id)))
            .FirstOrDefault();
        if (missingDependency is not null)
        {
            throw new CliInputException(
                $"Approved layout operation depends on unapproved operation '{missingDependency}'.");
        }
        Dictionary<string, FormatRule> rulesById = rules.Rules
            .ToDictionary(item => item.RuleId, StringComparer.Ordinal);
        bool selectedPageChange = approvedReview.Any(
            item => rulesById[item.RuleId].Target == RuleTarget.Page);
        bool pageConfirmed = options.Flag("confirm-page-changes");
        if (selectedPageChange && !pageConfirmed)
        {
            throw new CliInputException(
                "Approved page changes also require --confirm-page-changes.");
        }

        string[] approvedDirectiveIds = safe
            .Concat(approvedReview)
            .Select(item => item.DirectiveId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] issues = RepairPlanPolicy.ExpandApprovedIssues(
            plan,
            report,
            approvedDirectiveIds);
        string[] userConfirmedIssues = approvedReview
            .SelectMany(item => item.IssueIds)
            .Where(issueId => issues.Contains(issueId, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string original = Path.Combine(outputDirectory, "original.docx");
        string formatted = Path.Combine(outputDirectory, "formatted.docx");
        File.Copy(input, original, overwrite: false);
        RepairResult result = SafeRepairService.Execute(
            original,
            formatted,
            rules,
            report,
            new RepairSelection(
                issues,
                pageConfirmed,
                userConfirmedIssues));
        LayoutExecutionResult? layoutResult = approvedLayout.Length == 0
            ? null
            : IeeeLayoutConverter.Apply(formatted, approvedLayout);
        DocumentModel finalDocument = RequireDocument(
            WordDocumentParser.Parse(formatted));
        CheckReport finalCheck = new FormatCheckEngine().Check(
            finalDocument,
            rules,
            new DeterministicDocumentClassifier().Classify(
                finalDocument));
        IntegrityReport finalIntegrity =
            ContentIntegrityValidator.Compare(
                original,
                formatted,
                approvedLayout.Any(
                    item => item.Kind is
                        LayoutOperationKind.InsertContinuousSectionBreak
                        or LayoutOperationKind.InsertNextPageSectionBreak
                        or LayoutOperationKind.SetSectionColumns)
                    ? [IntegrityCheckIds.SectionTopology]
                    : Array.Empty<string>());
        bool finalReopened = layoutResult?.Reopened
            ?? result.ChangeLog.OutputReopened;
        bool finalPackageValid = result.ChangeLog.PackageValid
            && (layoutResult?.PackageValid ?? true);
        bool finalReady = result.ChangeLog.OriginalPreserved
            && finalReopened
            && finalPackageValid
            && finalIntegrity.Status == IntegrityStatus.Passed
            && result.ChangeLog.Entries.All(
                item => item.Status == RepairExecutionStatus.Applied)
            && (layoutResult?.ChangeLog.Entries.All(
                    item => item.Status
                        == RepairExecutionStatus.Applied)
                ?? true);
        var artifacts = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["original"] = "original.docx",
            ["formatted"] = "formatted.docx",
            ["repairPlan"] = "repair-plan.json",
            ["changeLog"] = "change-log.json",
            ["integrityReport"] = "integrity-report.json",
            ["postCheckJson"] = "post-check.json",
            ["postCheckHtml"] = "post-check.html",
        };
        if (layoutResult is not null)
        {
            artifacts["layoutChangeLog"] = "layout-change-log.json";
        }
        File.Copy(planPath, Path.Combine(
            outputDirectory,
            artifacts["repairPlan"]));
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, artifacts["changeLog"]),
            ChangeLogJson.Serialize(result.ChangeLog) + Environment.NewLine,
            token);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, artifacts["integrityReport"]),
            IntegrityReportJson.Serialize(finalIntegrity)
            + Environment.NewLine,
            token);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, artifacts["postCheckJson"]),
            CheckReportJson.Serialize(finalCheck)
            + Environment.NewLine,
            token);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, artifacts["postCheckHtml"]),
            CheckReportHtml.Render(finalCheck),
            token);
        if (layoutResult is not null)
        {
            await CliJson.WriteFileAsync(
                Path.Combine(
                    outputDirectory,
                    artifacts["layoutChangeLog"]),
                layoutResult.ChangeLog,
                token);
        }

        int experimental = plan.Directives.Count(
                item => item.Level == ModificationLevel.Experimental)
            + plan.LayoutOperations.Count(
                item => item.Level
                    == ModificationLevel.Experimental);
        bool reviewPending = approvedReview.Length != review.Length
            || approvedLayout.Length != layoutReview.Length;
        string status = !finalReady
            ? "failed"
            : reviewPending
                ? "needsConfirmation"
                : "passed";
        var manifest = new ApplyManifest(
            ApplyManifest.CurrentSchemaVersion,
            status,
            plan.PlanId,
            result.ChangeLog.OperationId,
            Hash(original),
            Hash(formatted),
            safe.Length,
            approvedReview.Length,
            experimental,
            result.ChangeLog.Entries.Count(
                item => item.Status == RepairExecutionStatus.Applied)
            + (layoutResult?.ChangeLog.Entries.Count(
                    item => item.Status
                        == RepairExecutionStatus.Applied)
                ?? 0),
            layoutResult?.ChangeLog.Entries.Count(
                item => item.Status == RepairExecutionStatus.Applied)
            ?? 0,
            result.ChangeLog.OriginalPreserved,
            finalReopened,
            finalPackageValid,
            Camel(finalIntegrity.Status),
            Status(finalCheck.Status),
            finalReady,
            artifacts);
        string manifestPath = Path.Combine(
            outputDirectory,
            "apply-manifest.json");
        await CliJson.WriteFileAsync(manifestPath, manifest, token);
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                status,
                new
                {
                    manifest = manifestPath,
                    formatted,
                    appliedChangeCount = manifest.AppliedChangeCount,
                    reviewDirectiveCount = review.Length,
                    approvedReviewDirectiveCount = approvedReview.Length,
                    reviewLayoutOperationCount = layoutReview.Length,
                    approvedLayoutOperationCount = approvedLayout.Length,
                    experimentalDirectiveCount = experimental,
                    readyForVisualValidation = manifest.ReadyForVisualValidation,
                    isReadyForUse = false,
                },
                Array.Empty<CliDiagnostic>(),
                finalReady
                    ? ["Render before/after pages and run visual validation."]
                    : ["Do not export: inspect failed deterministic or integrity gates."]),
            output,
            token);
        return !finalReady
            ? CliExitCodes.ValidationFailed
            : status == "needsConfirmation"
                ? CliExitCodes.NeedsConfirmation
                : CliExitCodes.Success;
    }

    private static async Task<int> AttemptInitAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string input = options.RequiredPath("input");
        string planPath = options.RequiredPath("plan");
        string outputDirectory = options.RequiredPath("output-dir");
        string attemptId = options.OptionalValue("attempt-id")
            ?? throw new CliInputException(
                "Option '--attempt-id' requires a value.");
        if (attemptId.Length > 80
            || attemptId.Any(
                character => !char.IsAsciiLetterOrDigit(character)
                    && character is not '-' and not '_' and not '.'))
        {
            throw new CliInputException(
                "Attempt IDs may contain only ASCII letters, digits, '.', '-' and '_', up to 80 characters.");
        }

        string[] selected = (options.OptionalValue(
                    "select-experimental")
                ?? throw new CliInputException(
                    "Option '--select-experimental' requires exact Experimental IDs."))
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
        if (selected.Length == 0
            || selected.Distinct(StringComparer.Ordinal).Count()
                != selected.Length)
        {
            throw new CliInputException(
                "Select at least one unique Experimental directive or operation ID.");
        }

        RepairPlan plan = RepairPlanJson.Deserialize(
            await File.ReadAllTextAsync(planPath, token));
        string sourceSha256 = Hash(input);
        if (!string.Equals(
                plan.SourceSha256,
                sourceSha256,
                StringComparison.Ordinal))
        {
            throw new CliInputException(
                "The validated plan is bound to a different source DOCX.");
        }

        HashSet<string> experimentalIds = plan.Directives
            .Where(item => item.Level == ModificationLevel.Experimental)
            .Select(item => item.DirectiveId)
            .Concat(
                plan.LayoutOperations
                    .Where(
                        item => item.Level
                            == ModificationLevel.Experimental)
                    .Select(item => item.OperationId))
            .ToHashSet(StringComparer.Ordinal);
        string? invalid = selected.FirstOrDefault(
            id => !experimentalIds.Contains(id));
        if (invalid is not null)
        {
            throw new CliInputException(
                $"'{invalid}' is not an Experimental item in this plan.");
        }

        EnsureNewDirectory(outputDirectory);
        string original = Path.Combine(outputDirectory, "original.docx");
        string candidate = Path.Combine(outputDirectory, "candidate.docx");
        string copiedPlan = Path.Combine(
            outputDirectory,
            "repair-plan.json");
        File.Copy(input, original, overwrite: false);
        File.Copy(input, candidate, overwrite: false);
        File.Copy(planPath, copiedPlan, overwrite: false);
        bool candidateReopened = WordDocumentParser.Parse(candidate).IsSuccess;
        string candidateSha256 = Hash(candidate);
        bool originalPreserved = string.Equals(
                Hash(original),
                sourceSha256,
                StringComparison.Ordinal)
            && string.Equals(
                Hash(input),
                sourceSha256,
                StringComparison.Ordinal);
        if (!candidateReopened || !originalPreserved)
        {
            throw new InvalidDataException(
                "The isolated attempt copy could not be reopened or did not preserve the source hash.");
        }

        string[] nextActions =
        [
            "Do not edit candidate.docx with an untyped or ad-hoc OOXML tool.",
            "Use only a future allow-listed Experimental executor that records exact operations and updates this attempt's evidence.",
            "This diagnostic attempt cannot be exported as ready in its initialized state.",
        ];
        var artifacts = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["original"] = "original.docx",
            ["candidate"] = "candidate.docx",
            ["repairPlan"] = "repair-plan.json",
            ["finalStatus"] = "FINAL_STATUS.md",
        };
        var manifest = new ExperimentalAttemptManifest(
            ExperimentalAttemptManifest.CurrentSchemaVersion,
            "isolatedDiagnostic",
            attemptId,
            plan.PlanId,
            plan.SourceReportId,
            sourceSha256,
            candidateSha256,
            selected.Order(StringComparer.Ordinal).ToArray(),
            originalPreserved,
            candidateReopened,
            ReadyForUse: false,
            artifacts,
            nextActions);
        await CliJson.WriteFileAsync(
            Path.Combine(
                outputDirectory,
                "experimental-attempt.json"),
            manifest,
            token);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "FINAL_STATUS.md"),
            $"""
            # PaperFormat Experimental attempt

            - Attempt: `{attemptId}`
            - Status: `isolatedDiagnostic`
            - Plan: `{plan.PlanId}`
            - Source SHA-256: `{sourceSha256}`
            - Candidate SHA-256: `{candidateSha256}`
            - Ready for use: `false`

            This directory is an isolated diagnostic attempt. No Experimental
            mutation has been executed, and `candidate.docx` is not a ready
            manuscript. Ordinary `apply` and `export` cannot promote it.
            """ + Environment.NewLine,
            token);
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                "needsConfirmation",
                manifest,
                [
                    new CliDiagnostic(
                        "experimental_diagnostic_only",
                        "warning",
                        "The isolated attempt contains no executable Experimental mutation and is not ready for use."),
                ],
                nextActions),
            output,
            token);
        return CliExitCodes.NeedsConfirmation;
    }

    private async Task<int> RenderAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string input = options.RequiredPath("input");
        string outputDirectory = options.RequiredPath("output-dir");
        if (!_renderer.IsAvailable)
        {
            throw new InvalidOperationException(
                "LibreOffice and pdftoppm are required for rendering.");
        }

        RenderManifest manifest = await RenderDocumentAsync(
            input,
            outputDirectory,
            token);
        string manifestPath = Path.Combine(
            outputDirectory,
            "render-manifest.json");
        await CliJson.WriteFileAsync(manifestPath, manifest, token);
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                "passed",
                new
                {
                    manifest = manifestPath,
                    pageCount = manifest.Pages.Count,
                    pdf = manifest.Pdf,
                },
                Array.Empty<CliDiagnostic>(),
                ["Inspect every rendered page before approving layout changes."]),
            output,
            token);
        return CliExitCodes.Success;
    }

    private static async Task<int> ComparePagesAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string before = options.RequiredPath("before");
        string after = options.RequiredPath("after");
        string outputPath = options.RequiredPath("output");
        PageComparisonReport report = PageComparer.Compare(before, after);
        await CliJson.WriteFileAsync(outputPath, report, token);
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                report.Status,
                new
                {
                    output = outputPath,
                    report.BeforePageCount,
                    report.AfterPageCount,
                    findingCount = report.Findings.Count,
                },
                Array.Empty<CliDiagnostic>(),
                report.Status == "passed"
                    ? ["Complete semantic visual review before export; pagination changes are advisory only."]
                    : ["Review blocking blank-region or page-dimension findings."]),
            output,
            token);
        return report.Status switch
        {
            "passed" => CliExitCodes.Success,
            "needsConfirmation" => CliExitCodes.NeedsConfirmation,
            _ => CliExitCodes.ValidationFailed,
        };
    }

    private static async Task<int> VisualReviewAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string applyPath = options.RequiredPath("apply-manifest");
        string beforeDirectory = options.RequiredPath("before-render");
        string afterDirectory = options.RequiredPath("after-render");
        string comparisonPath = options.RequiredPath("comparison");
        string submissionPath = options.RequiredPath("submission");
        string outputPath = options.RequiredPath("output");
        ApplyManifest apply = System.Text.Json.JsonSerializer.Deserialize<
            ApplyManifest>(
            await File.ReadAllTextAsync(applyPath, token),
            CliJson.Options)
            ?? throw new InvalidDataException(
                "The apply manifest was empty.");
        RenderManifest before = await ReadJsonAsync<RenderManifest>(
            Path.Combine(beforeDirectory, "render-manifest.json"),
            token);
        RenderManifest after = await ReadJsonAsync<RenderManifest>(
            Path.Combine(afterDirectory, "render-manifest.json"),
            token);
        PageComparisonReport comparison =
            await ReadJsonAsync<PageComparisonReport>(
                comparisonPath,
                token);
        ExternalVisualReviewSubmission submission =
            await ReadJsonAsync<ExternalVisualReviewSubmission>(
                submissionPath,
                token);
        if (submission.SchemaVersion
                != ExternalVisualReviewSubmission.CurrentSchemaVersion
            || submission.PlanId != apply.PlanId
            || submission.OperationId != apply.OperationId
            || before.SourceSha256 != apply.SourceSha256
            || after.SourceSha256 != apply.OutputSha256
            || before.Pages.Count != comparison.BeforePageCount
            || after.Pages.Count != comparison.AfterPageCount
            || submission.SourcePageCount != before.Pages.Count
            || submission.OutputPageCount != after.Pages.Count)
        {
            throw new CliInputException(
                "The visual-review submission is stale or does not match the rendered evidence.");
        }

        if (submission.Status == VisualReviewStatus.NotRun)
        {
            throw new CliInputException(
                "A visual-review submission must be passed, needsReview, or failed.");
        }

        var findings = submission.Findings.ToList();
        VisualReviewStatus status = submission.Status;
        if (comparison.Findings.Any(
                item => item.Severity == "blocking"))
        {
            if (status == VisualReviewStatus.Passed)
            {
                status = VisualReviewStatus.NeedsReview;
            }

            findings.Add(
                new VisualReviewFinding(
                    "deterministic_page_anomaly",
                    RepairPlanRisk.High,
                    "The deterministic page analyzer found a blocking anomaly that requires another iteration."));
        }

        if (status == VisualReviewStatus.Passed
            && findings.Any(
                item => item.Risk is
                    RepairPlanRisk.High or RepairPlanRisk.Blocked))
        {
            status = VisualReviewStatus.NeedsReview;
        }

        var review = new VisualReviewReport(
            status,
            submission.ProviderId,
            submission.Model,
            before.Pages.Count,
            after.Pages.Count,
            findings,
            submission.Summary);
        var validated = new ValidatedVisualReview(
            ValidatedVisualReview.CurrentSchemaVersion,
            apply.PlanId,
            apply.OperationId,
            apply.SourceSha256,
            apply.OutputSha256,
            EvidenceBound: true,
            review);
        await CliJson.WriteFileAsync(outputPath, validated, token);
        string cliStatus = status switch
        {
            VisualReviewStatus.Passed => "passed",
            VisualReviewStatus.NeedsReview => "needsConfirmation",
            _ => "failed",
        };
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                cliStatus,
                new
                {
                    output = outputPath,
                    review.Status,
                    sourcePageCount = before.Pages.Count,
                    outputPageCount = after.Pages.Count,
                    findingCount = review.Findings.Count,
                    validated.EvidenceBound,
                },
                Array.Empty<CliDiagnostic>(),
                status == VisualReviewStatus.Passed
                    ? ["Run validate-output with this evidence-bound review."]
                    : ["Resolve visual findings, regenerate the candidate, and review again."]),
            output,
            token);
        return status switch
        {
            VisualReviewStatus.Passed => CliExitCodes.Success,
            VisualReviewStatus.NeedsReview =>
                CliExitCodes.NeedsConfirmation,
            _ => CliExitCodes.ValidationFailed,
        };
    }

    private static async Task<int> ValidateIntegrityAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string source = options.RequiredPath("source");
        string candidate = options.RequiredPath("candidate");
        string outputPath = options.RequiredPath("output");
        string sourceHashBefore = Hash(source);
        DocumentParseResult candidateParse =
            WordDocumentParser.Parse(candidate);
        IntegrityReport integrity =
            ContentIntegrityValidator.Compare(source, candidate);
        bool sourcePreserved = string.Equals(
            sourceHashBefore,
            Hash(source),
            StringComparison.Ordinal);
        bool reopened = candidateParse.IsSuccess;
        string status = !sourcePreserved
            || !reopened
            || integrity.Status == IntegrityStatus.Failed
            ? "failed"
            : integrity.Status == IntegrityStatus.NeedsConfirmation
                ? "needsConfirmation"
                : "passed";
        string[] reasons =
        [
            .. !sourcePreserved
                ? new[] { "The source file hash changed." }
                : Array.Empty<string>(),
            .. !reopened
                ? new[] { "The candidate DOCX could not be reopened." }
                : Array.Empty<string>(),
            .. integrity.Checks
                .Where(item => item.Status != IntegrityStatus.Passed)
                .Select(item => $"{item.CheckId}: {item.Message}"),
        ];
        var report = new ValidationReport(
            ValidationReport.CurrentSchemaVersion,
            status,
            sourcePreserved,
            reopened,
            integrity,
            null,
            reasons);
        await CliJson.WriteFileAsync(outputPath, report, token);
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                status,
                new
                {
                    output = outputPath,
                    sourcePreserved,
                    candidateReopened = reopened,
                    integrityStatus = integrity.Status,
                },
                Array.Empty<CliDiagnostic>(),
                status == "passed"
                    ? ["Run rendered before/after comparison."]
                    : ["Do not export the candidate as ready."]),
            output,
            token);
        return status switch
        {
            "passed" => CliExitCodes.Success,
            "needsConfirmation" => CliExitCodes.NeedsConfirmation,
            _ => CliExitCodes.ValidationFailed,
        };
    }

    private static async Task<int> ValidateOutputAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string inputDirectory = options.RequiredPath("input-dir");
        string comparisonPath = options.RequiredPath("comparison");
        string visualPath = options.RequiredPath("visual-review");
        string outputPath = options.RequiredPath("output");
        ApplyManifest apply = await ReadJsonAsync<ApplyManifest>(
            Path.Combine(inputDirectory, "apply-manifest.json"),
            token);
        string original = Path.Combine(inputDirectory, "original.docx");
        string formatted = Path.Combine(inputDirectory, "formatted.docx");
        PageComparisonReport comparison =
            await ReadJsonAsync<PageComparisonReport>(
                comparisonPath,
                token);
        ValidatedVisualReview visual =
            await ReadValidatedVisualReviewAsync(visualPath, token);
        CheckReport postCheck = CheckReportJson.Deserialize(
            await File.ReadAllTextAsync(
                Path.Combine(inputDirectory, "post-check.json"),
                token));
        IntegrityReport integrity = ContentIntegrityValidator.Compare(
            original,
            formatted,
            apply.AppliedLayoutOperationCount > 0
                ? [IntegrityCheckIds.SectionTopology]
                : Array.Empty<string>());
        bool sourcePreserved = Hash(original) == apply.SourceSha256;
        bool candidateReopened =
            WordDocumentParser.Parse(formatted).IsSuccess;
        bool hashesBound = Hash(formatted) == apply.OutputSha256
            && visual.SourceSha256 == apply.SourceSha256
            && visual.OutputSha256 == apply.OutputSha256;
        bool identityBound = visual.PlanId == apply.PlanId
            && visual.OperationId == apply.OperationId
            && visual.EvidenceBound;
        bool pagesPassed = comparison.Status != "failed"
            && !comparison.Findings.Any(
                item => item.Severity == "blocking");
        bool postCheckPassed = postCheck.Summary.ErrorCount == 0;
        string[] reasons =
        [
            .. !apply.ReadyForVisualValidation
                ? ["The apply result did not pass deterministic gates."]
                : Array.Empty<string>(),
            .. !sourcePreserved
                ? ["The preserved source hash does not match the apply manifest."]
                : Array.Empty<string>(),
            .. !candidateReopened
                ? ["The formatted DOCX could not be reopened."]
                : Array.Empty<string>(),
            .. !hashesBound
                ? ["The visual evidence hashes are stale or mismatched."]
                : Array.Empty<string>(),
            .. !identityBound
                ? ["The visual review belongs to a different plan or operation."]
                : Array.Empty<string>(),
            .. integrity.Status != IntegrityStatus.Passed
                ? ["Content integrity did not pass."]
                : Array.Empty<string>(),
            .. !postCheckPassed
                ? ["The post-repair format check still has errors."]
                : Array.Empty<string>(),
            .. !pagesPassed
                ? ["The deterministic rendered-page comparison has blocking findings."]
                : Array.Empty<string>(),
            .. visual.Review.Status != VisualReviewStatus.Passed
                ? ["Semantic visual review did not pass."]
                : Array.Empty<string>(),
        ];
        bool ready = reasons.Length == 0;
        var validation = new ValidationReport(
            ValidationReport.CurrentSchemaVersion,
            ready ? "passed" : "failed",
            sourcePreserved,
            candidateReopened,
            integrity,
            comparison,
            reasons,
            visual.Review,
            apply.PlanId,
            apply.OperationId);
        await CliJson.WriteFileAsync(outputPath, validation, token);
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                validation.Status,
                new
                {
                    output = outputPath,
                    readyForExport = ready,
                    blockingReasonCount = reasons.Length,
                    postCheckErrorCount =
                        postCheck.Summary.ErrorCount,
                    postCheckAdvisoryElementCount =
                        postCheck.Summary.PendingElementCount,
                    visualReviewStatus = visual.Review.Status,
                },
                Array.Empty<CliDiagnostic>(),
                ready
                    ? ["Copy validation-report.json into the apply directory and run export."]
                    : ["Do not export; resolve every blocking reason."]),
            output,
            token);
        return ready
            ? CliExitCodes.Success
            : CliExitCodes.ValidationFailed;
    }

    private static async Task<int> ExportAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string inputDirectory = options.RequiredPath("input-dir");
        string outputDirectory = options.RequiredPath("output-dir");
        string applyManifestPath = Path.Combine(
            inputDirectory,
            "apply-manifest.json");
        if (!File.Exists(applyManifestPath)
            && File.Exists(
                Path.Combine(
                    inputDirectory,
                    "experimental-attempt.json")))
        {
            throw new CliInputException(
                "An initialized Experimental attempt is diagnostic-only and cannot be exported as ready.");
        }

        ApplyManifest apply = System.Text.Json.JsonSerializer.Deserialize<
            ApplyManifest>(
            await File.ReadAllTextAsync(applyManifestPath, token),
            CliJson.Options)
            ?? throw new InvalidDataException(
                "The apply manifest was empty.");
        if (!apply.ReadyForVisualValidation
            || string.Equals(
                apply.Status,
                "failed",
                StringComparison.Ordinal))
        {
            throw new CliInputException(
                "The apply result did not pass deterministic and integrity gates.");
        }

        string[] required =
        [
            "original.docx",
            "formatted.docx",
            "repair-plan.json",
            "change-log.json",
            "integrity-report.json",
            "post-check.json",
            "post-check.html",
            "apply-manifest.json",
        ];
        foreach (string fileName in required)
        {
            string source = Path.Combine(inputDirectory, fileName);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    $"Required export artifact '{fileName}' is missing.",
                    source);
            }
        }

        string validationSource = Path.Combine(
            inputDirectory,
            "validation-report.json");
        if (!File.Exists(validationSource))
        {
            throw new CliInputException(
                "A passed validation-report.json is required before export.");
        }

        using System.Text.Json.JsonDocument validation =
            System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(validationSource, token));
        System.Text.Json.JsonElement root = validation.RootElement;
        bool validationPassed =
            root.GetProperty("status").GetString() == "passed"
            && root.GetProperty("sourcePreserved").GetBoolean()
            && root.GetProperty("candidateReopened").GetBoolean()
            && root.GetProperty("blockingReasons").GetArrayLength() == 0
            && root.TryGetProperty(
                "planId",
                out System.Text.Json.JsonElement planId)
            && planId.GetString() == apply.PlanId
            && root.TryGetProperty(
                "operationId",
                out System.Text.Json.JsonElement operationId)
            && operationId.GetString() == apply.OperationId
            && root.TryGetProperty(
                "visualReview",
                out System.Text.Json.JsonElement review)
            && review.GetProperty("status").GetString() == "passed"
            && Hash(Path.Combine(inputDirectory, "original.docx"))
                == apply.SourceSha256
            && Hash(Path.Combine(inputDirectory, "formatted.docx"))
                == apply.OutputSha256;
        if (!validationPassed)
        {
            throw new CliInputException(
                "The candidate failed final validation and cannot be exported as ready.");
        }

        EnsureNewDirectory(outputDirectory);
        var artifacts = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (string fileName in required)
        {
            string source = Path.Combine(inputDirectory, fileName);
            File.Copy(
                source,
                Path.Combine(outputDirectory, fileName),
                overwrite: false);
            artifacts[Path.GetFileNameWithoutExtension(fileName)] =
                fileName;
        }

        foreach (string optional in new[]
                 {
                     "layout-change-log.json",
                     "page-comparison.json",
                     "validated-visual-review.json",
                 })
        {
            string source = Path.Combine(inputDirectory, optional);
            if (!File.Exists(source))
            {
                continue;
            }

            File.Copy(
                source,
                Path.Combine(outputDirectory, optional),
                overwrite: false);
            artifacts[Path.GetFileNameWithoutExtension(optional)] =
                optional;
        }

        File.Copy(
            validationSource,
            Path.Combine(outputDirectory, "validation-report.json"),
            overwrite: false);
        artifacts["validation-report"] = "validation-report.json";
        const string status = "ready";
        string[] remaining = Array.Empty<string>();
        var manifest = new ExportManifest(
            ExportManifest.CurrentSchemaVersion,
            status,
            apply.SourceSha256,
            apply.OutputSha256,
            artifacts,
            remaining);
        await CliJson.WriteFileAsync(
            Path.Combine(outputDirectory, "export-manifest.json"),
            manifest,
            token);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "FINAL_STATUS.md"),
            $"""
            # PaperFormat export status

            - Status: `{status}`
            - Source SHA-256: `{apply.SourceSha256}`
            - Output SHA-256: `{apply.OutputSha256}`
            - Applied changes: `{apply.AppliedChangeCount}`

            All recorded deterministic, integrity, and visual validation gates passed.
            """
            + Environment.NewLine,
            token);
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                "passed",
                new
                {
                    outputDirectory,
                    manifest = Path.Combine(
                        outputDirectory,
                        "export-manifest.json"),
                    status,
                },
                Array.Empty<CliDiagnostic>(),
                ["Deliver formatted.docx with the complete proof bundle."]),
            output,
            token);
        return CliExitCodes.Success;
    }

    private async Task<int> RunWorkflowAsync(
        string command,
        Arguments options,
        TextWriter output,
        CancellationToken token)
    {
        string manuscript = options.RequiredPath("manuscript");
        string workspace = options.RequiredPath("workspace");
        string? template = options.OptionalPath("template");
        string? rulesPath = options.OptionalPath("rules");
        bool ieee = options.Flag("ieee");
        int formatSourceCount = (ieee ? 1 : 0)
            + (template is null ? 0 : 1)
            + (rulesPath is null ? 0 : 1);
        if (formatSourceCount != 1)
        {
            throw new CliInputException(
                "Choose exactly one of --template TEMPLATE, --rules JSON, or --ieee.");
        }

        EnsureNewDirectory(workspace);
        string original = Path.Combine(workspace, "original.docx");
        File.Copy(manuscript, original, overwrite: false);
        DocumentParseResult parse = WordDocumentParser.Parse(original);
        DocumentModel document = RequireDocument(parse);
        DocumentInspection inspection =
            WordDocumentInspector.Inspect(original);
        RulePackage rules = ieee
            ? new BuiltInIeeeRuleProvider().Extract(
                new BuiltInFormatRequirementSource(
                    BuiltInIeeeRuleProvider.ProfileId))
            : template is not null
                ? DeriveRules(template)
                : RulePackageJson.Deserialize(
                    await File.ReadAllTextAsync(rulesPath!, token));
        ClassificationSet classifications =
            new DeterministicDocumentClassifier().Classify(document);
        LayoutAnalysis layoutAnalysis = IeeeLayoutAnalyzer.Analyze(
            original,
            rules,
            classifications);
        CheckReport check = new FormatCheckEngine().Check(
            document,
            rules,
            classifications);

        var artifacts = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["original"] = "original.docx",
            ["documentModel"] = "document-model.json",
            ["formatSpec"] = "format-spec.json",
            ["classifications"] = "classifications.json",
            ["issueReportJson"] = "issue-report.json",
            ["issueReportHtml"] = "issue-report.html",
            ["planCandidates"] = "plan-candidates.json",
            ["layoutAnalysis"] = "layout-analysis.json",
        };
        await CliJson.WriteFileAsync(
            Path.Combine(workspace, artifacts["documentModel"]),
            new { inspection, documentModel = document },
            token);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, artifacts["formatSpec"]),
            RulePackageJson.Serialize(rules) + Environment.NewLine,
            token);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, artifacts["classifications"]),
            ClassificationJson.Serialize(classifications)
            + Environment.NewLine,
            token);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, artifacts["issueReportJson"]),
            CheckReportJson.Serialize(check) + Environment.NewLine,
            token);
        await File.WriteAllTextAsync(
            Path.Combine(workspace, artifacts["issueReportHtml"]),
            CheckReportHtml.Render(check),
            token);
        await CliJson.WriteFileAsync(
            Path.Combine(workspace, artifacts["planCandidates"]),
            new
            {
                schemaVersion = AgentPlanProposal.CurrentSchemaVersion,
                sourceReportId = check.ReportId,
                sourceSha256 = inspection.SourceSha256,
                rulePackageId = rules.PackageId,
                rulePackageRevision = rules.Revision,
                candidateScopes =
                    RepairPlanPolicy.CreateCandidateGroups(check, rules),
            },
            token);
        await CliJson.WriteFileAsync(
            Path.Combine(workspace, artifacts["layoutAnalysis"]),
            layoutAnalysis,
            token);

        List<string> nextActions = [
            "Review classifications and exact format issues.",
            "Create and validate a typed RepairPlan before mutation.",
        ];
        if (layoutAnalysis.CanConvert)
        {
            nextActions.Add(
                "Propose Review layout operations using the exact front-matter boundary.");
        }
        else if (layoutAnalysis.Blockers.Count > 0)
        {
            nextActions.Add(
                "Resolve layout-analysis blockers before structural conversion.");
        }
        if (_renderer.IsAvailable)
        {
            string renderDirectory = Path.Combine(
                workspace,
                "before-pages");
            RenderManifest render = await RenderDocumentAsync(
                original,
                renderDirectory,
                token);
            await CliJson.WriteFileAsync(
                Path.Combine(renderDirectory, "render-manifest.json"),
                render,
                token);
            artifacts["beforePages"] = "before-pages";
        }
        else
        {
            nextActions.Add(
                "Install LibreOffice and pdftoppm before visual planning.");
        }

        string taskId = "task-" + inspection.SourceSha256[..16];
        string status = check.Status == CheckStatus.NeedsConfirmation
            ? "needsConfirmation"
            : check.Status == CheckStatus.Passed
                ? "passed"
                : "issuesFound";
        var manifest = new WorkflowManifest(
            WorkflowManifest.CurrentSchemaVersion,
            taskId,
            status,
            inspection.SourceSha256,
            ieee
                ? BuiltInIeeeRuleProvider.ProfileId
                : Path.GetFileName(template ?? rulesPath!),
            artifacts,
            nextActions);
        await CliJson.WriteFileAsync(
            Path.Combine(workspace, "workflow.json"),
            manifest,
            token);
        await WriteFinalStatusAsync(
            workspace,
            manifest,
            check,
            token);
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                status,
                manifest,
                Array.Empty<CliDiagnostic>(),
                nextActions),
            output,
            token);
        return check.Status == CheckStatus.NeedsConfirmation
            ? CliExitCodes.NeedsConfirmation
            : CliExitCodes.Success;
    }

    private async Task<RenderManifest> RenderDocumentAsync(
        string input,
        string outputDirectory,
        CancellationToken token)
    {
        RenderedDocument rendered = await _renderer.RenderAsync(
            input,
            outputDirectory,
            token);
        RenderPageManifest[] pages = rendered.Pages
            .Select(
                page =>
                {
                    (int width, int height, string sha256) =
                        PngInspector.Inspect(page.ImagePath);
                    return new RenderPageManifest(
                        page.PageNumber,
                        Path.GetRelativePath(
                            outputDirectory,
                            page.ImagePath),
                        sha256,
                        width,
                        height);
                })
            .ToArray();
        return new RenderManifest(
            RenderManifest.CurrentSchemaVersion,
            Hash(input),
            _renderer.GetType().Name,
            120,
            Path.GetRelativePath(outputDirectory, rendered.PdfPath),
            pages);
    }

    private static RulePackage DeriveRules(string template)
    {
        DocumentModel document = RequireDocument(
            WordDocumentParser.Parse(template));
        return new WordTemplateRuleProvider().Extract(
            new WordTemplateFormatRequirementSource(
                Path.GetFileName(template),
                document));
    }

    private static async Task<ClassificationSet> ClassificationsAsync(
        Arguments options,
        DocumentModel document,
        CancellationToken token)
    {
        string? path = options.OptionalPath("classifications");
        return path is null
            ? new DeterministicDocumentClassifier().Classify(document)
            : ClassificationJson.Deserialize(
                await File.ReadAllTextAsync(path, token));
    }

    private static DocumentModel RequireDocument(DocumentParseResult parse)
    {
        if (parse.Document is not null && parse.IsSuccess)
        {
            return parse.Document;
        }

        string detail = string.Join(
            "; ",
            parse.Diagnostics
                .Where(
                    item => item.Severity
                        == ParseDiagnosticSeverity.Error)
                .Select(item => $"{item.Code}: {item.Message}"));
        throw new InvalidDataException(
            detail.Length == 0
                ? "The Word package could not be parsed."
                : detail);
    }

    private static void EnsureNewDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath)
            && Directory.EnumerateFileSystemEntries(fullPath).Any())
        {
            throw new CliInputException(
                $"Output directory must be new or empty: {fullPath}");
        }

        Directory.CreateDirectory(fullPath);
    }

    private static async Task<T> ReadJsonAsync<T>(
        string path,
        CancellationToken token)
    {
        string json = await File.ReadAllTextAsync(path, token);
        return System.Text.Json.JsonSerializer.Deserialize<T>(
                json,
                CliJson.Options)
            ?? throw new InvalidDataException(
                $"'{Path.GetFileName(path)}' was empty or invalid.");
    }

    private static async Task<ValidatedVisualReview>
        ReadValidatedVisualReviewAsync(
            string path,
            CancellationToken token)
    {
        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(path, token));
        System.Text.Json.JsonElement root = document.RootElement;
        System.Text.Json.JsonElement reviewElement =
            root.GetProperty("review");
        VisualReviewStatus status =
            System.Text.Json.JsonSerializer.Deserialize<VisualReviewStatus>(
                reviewElement.GetProperty("status").GetRawText(),
                CliJson.Options);
        RepairPlanRisk Risk(System.Text.Json.JsonElement item) =>
            System.Text.Json.JsonSerializer.Deserialize<RepairPlanRisk>(
                item.GetProperty("risk").GetRawText(),
                CliJson.Options);
        VisualReviewFinding[] findings = reviewElement
            .GetProperty("findings")
            .EnumerateArray()
            .Select(
                item => new VisualReviewFinding(
                    item.GetProperty("code").GetString()!,
                    Risk(item),
                    item.GetProperty("message").GetString()!,
                    OptionalPage(item, "sourcePage"),
                    OptionalPage(item, "outputPage")))
            .ToArray();
        var review = new VisualReviewReport(
            status,
            reviewElement.GetProperty("providerId").GetString()!,
            reviewElement.GetProperty("model").GetString()!,
            reviewElement.GetProperty("sourcePageCount").GetInt32(),
            reviewElement.GetProperty("outputPageCount").GetInt32(),
            findings,
            reviewElement.TryGetProperty(
                    "summary",
                    out System.Text.Json.JsonElement summary)
                && summary.ValueKind
                    != System.Text.Json.JsonValueKind.Null
                    ? summary.GetString()
                    : null);
        return new ValidatedVisualReview(
            root.GetProperty("schemaVersion").GetString()!,
            root.GetProperty("planId").GetString()!,
            root.GetProperty("operationId").GetString()!,
            root.GetProperty("sourceSha256").GetString()!,
            root.GetProperty("outputSha256").GetString()!,
            root.GetProperty("evidenceBound").GetBoolean(),
            review);

        static int? OptionalPage(
            System.Text.Json.JsonElement finding,
            string propertyName) =>
            finding.TryGetProperty(
                    propertyName,
                    out System.Text.Json.JsonElement page)
                && page.ValueKind
                    != System.Text.Json.JsonValueKind.Null
                    ? page.GetInt32()
                    : null;
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
    }

    private static string Status(CheckStatus status) => status switch
    {
        CheckStatus.Passed => "passed",
        CheckStatus.NeedsConfirmation => "needsConfirmation",
        CheckStatus.IssuesFound => "issuesFound",
        _ => throw new InvalidOperationException(
            $"Unknown check status '{status}'."),
    };

    private static string Camel(IntegrityStatus status) => status switch
    {
        IntegrityStatus.Passed => "passed",
        IntegrityStatus.NeedsConfirmation => "needsConfirmation",
        IntegrityStatus.Failed => "failed",
        _ => throw new InvalidOperationException(
            $"Unknown integrity status '{status}'."),
    };

    private static async Task WriteSuccessAsync(
        string command,
        object data,
        string? outputPath,
        TextWriter output,
        CancellationToken token)
    {
        if (outputPath is not null)
        {
            await CliJson.WriteFileAsync(outputPath, data, token);
        }

        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                "passed",
                outputPath is null
                    ? data
                    : new { output = outputPath },
                Array.Empty<CliDiagnostic>(),
                Array.Empty<string>()),
            output,
            token);
    }

    private static async Task<int> InvalidAsync(
        string command,
        string message,
        TextWriter error) =>
        await FailureAsync(
            command,
            "invalid_arguments",
            message,
            CliExitCodes.InvalidInput,
            error);

    private static async Task<int> FailureAsync(
        string command,
        string code,
        string message,
        int exitCode,
        TextWriter error)
    {
        await WriteEnvelopeAsync(
            new CliResult(
                CliResult.CurrentSchemaVersion,
                command,
                "failed",
                null,
                [new CliDiagnostic(code, "error", message)],
                ["Resolve the reported error and rerun the same command."]),
            error,
            CancellationToken.None);
        return exitCode;
    }

    private static async Task WriteEnvelopeAsync(
        CliResult result,
        TextWriter writer,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await writer.WriteLineAsync(CliJson.Serialize(result));
    }

    private static async Task WriteFinalStatusAsync(
        string workspace,
        WorkflowManifest manifest,
        CheckReport report,
        CancellationToken token)
    {
        string text =
            $"""
            # PaperFormat task status

            - Task: `{manifest.TaskId}`
            - Status: `{manifest.Status}`
            - Source SHA-256: `{manifest.SourceSha256}`
            - Format source: `{manifest.FormatSource}`
            - Check score: `{report.Summary.Score}`
            - Issues: `{report.Summary.IssueCount}`
            - Pending elements: `{report.Summary.PendingElementCount}`

            This check-only task has not modified the manuscript.
            A formatted result must not be exported until a typed plan is
            validated, approved operations are applied to a copy, and every
            integrity and rendered-page gate passes.
            """;
        await File.WriteAllTextAsync(
            Path.Combine(workspace, "FINAL_STATUS.md"),
            text + Environment.NewLine,
            token);
    }

    private static string Help() =>
        """
        PaperFormat Master Agent-native CLI

        Usage:
          paperformat inspect --input FILE [--output JSON]
          paperformat derive-template (--input TEMPLATE | --ieee) --output JSON
          paperformat classify --input FILE --output JSON
          paperformat layout-analyze --input FILE --rules JSON [--classifications JSON] --output JSON
          paperformat check --input FILE --rules JSON [--classifications JSON] --output-dir DIR
          paperformat plan-validate --source FILE --report JSON --rules JSON --proposal JSON --output JSON
          paperformat apply --input FILE --rules JSON --report JSON --plan JSON --output-dir DIR [--approve ID,ID] [--approve-all-review] [--confirm-page-changes]
          paperformat attempt-init --input FILE --plan JSON --attempt-id ID --select-experimental ID,ID --output-dir DIR
          paperformat render --input FILE --output-dir DIR
          paperformat compare-pages --before DIR --after DIR --output JSON
          paperformat visual-review --apply-manifest JSON --before-render DIR --after-render DIR --comparison JSON --submission JSON --output JSON
          paperformat validate-integrity --source FILE --candidate FILE --output JSON
          paperformat validate-output --input-dir DIR --comparison JSON --visual-review JSON --output JSON
          paperformat export --input-dir DIR --output-dir DIR
          paperformat run-workflow --manuscript FILE (--template FILE | --rules JSON | --ieee) --workspace DIR

        Exit codes:
          0  command completed
          2  invalid input or arguments
          3  explicit confirmation is required
          4  validation failed
          5  required local tool is unavailable
          10 unexpected failure

        Modification levels:
          Advisory      reported or preserved; never executed and never approval-blocking
          Safe          deterministic and automatic
          Review        exact directive IDs require user approval
          Experimental  isolated diagnostic attempt; never executed by ordinary apply
        """;

    private sealed class Arguments
    {
        private readonly Dictionary<string, string?> _values;

        private Arguments(Dictionary<string, string?> values)
        {
            _values = values;
        }

        public static Arguments Parse(string[] args)
        {
            var values = new Dictionary<string, string?>(
                StringComparer.Ordinal);
            for (int index = 0; index < args.Length; index++)
            {
                string item = args[index];
                if (!item.StartsWith("--", StringComparison.Ordinal)
                    || item.Length == 2)
                {
                    throw new CliInputException(
                        $"Unexpected argument '{item}'.");
                }

                string key = item[2..];
                if (values.ContainsKey(key))
                {
                    throw new CliInputException(
                        $"Option '--{key}' was provided more than once.");
                }

                if (index + 1 < args.Length
                    && !args[index + 1].StartsWith(
                        "--",
                        StringComparison.Ordinal))
                {
                    values[key] = args[++index];
                }
                else
                {
                    values[key] = null;
                }
            }

            return new Arguments(values);
        }

        public string RequiredPath(string name) =>
            OptionalPath(name)
            ?? throw new CliInputException(
                $"Option '--{name}' requires a value.");

        public string? OptionalPath(string name)
        {
            if (!_values.TryGetValue(name, out string? value))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new CliInputException(
                    $"Option '--{name}' requires a value.");
            }

            return Path.GetFullPath(value);
        }

        public string? OptionalValue(string name)
        {
            if (!_values.TryGetValue(name, out string? value))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new CliInputException(
                    $"Option '--{name}' requires a value.");
            }

            return value;
        }

        public bool Flag(string name)
        {
            if (!_values.TryGetValue(name, out string? value))
            {
                return false;
            }

            if (value is not null)
            {
                throw new CliInputException(
                    $"Option '--{name}' does not accept a value.");
            }

            return true;
        }
    }

    private sealed class CliInputException(string message)
        : Exception(message);
}
