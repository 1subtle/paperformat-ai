namespace PaperFormat.Domain;

/// <summary>
/// Decision proposed for one protected scope of format issues.
/// </summary>
public enum RepairPlanDecision
{
    Apply,
    Preserve,
    ReportOnly,
}

/// <summary>
/// Risk assigned to a proposed protected scope of deterministic repairs.
/// </summary>
public enum RepairPlanRisk
{
    Low,
    Medium,
    High,
    Blocked,
}

/// <summary>
/// Execution boundary enforced by PaperFormat after an Agent proposes work.
/// </summary>
public enum ModificationLevel
{
    /// <summary>
    /// A non-executable observation that does not require user approval.
    /// </summary>
    Advisory,

    /// <summary>
    /// Deterministic, allow-listed, reversible formatting may run unattended.
    /// </summary>
    Safe,

    /// <summary>
    /// Deterministic formatting may run only after explicit user approval.
    /// </summary>
    Review,

    /// <summary>
    /// Structural or insufficiently proven work is report-only by default.
    /// </summary>
    Experimental,
}

/// <summary>
/// Origin of a repair plan.
/// </summary>
public enum RepairPlanOrigin
{
    OpenAi,
    ExternalAgent,
    ConservativeFallback,
}

/// <summary>
/// One validated AI recommendation for an exact, bounded issue scope.
/// </summary>
public sealed record RepairPlanDirective
{
    public RepairPlanDirective(
        string directiveId,
        string scopeId,
        string scope,
        string ruleId,
        RepairPlanDecision decision,
        RepairPlanRisk risk,
        decimal confidence,
        IEnumerable<string> issueIds,
        bool requiresUserConfirmation,
        string reason,
        IEnumerable<string>? safetyNotes = null,
        ModificationLevel level = ModificationLevel.Review,
        IEnumerable<string>? dependsOnScopeIds = null,
        string rollbackStrategy = "discardCandidateCopy")
    {
        DirectiveId = DomainGuard.RequiredIdentifier(
            directiveId,
            nameof(directiveId));
        ScopeId = DomainGuard.RequiredIdentifier(scopeId, nameof(scopeId));
        Scope = DomainGuard.RequiredIdentifier(scope, nameof(scope));
        RuleId = DomainGuard.RequiredIdentifier(ruleId, nameof(ruleId));
        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                confidence,
                "Repair-plan confidence must be between zero and one.");
        }

        string[] normalizedIssueIds = (
            issueIds ?? throw new ArgumentNullException(nameof(issueIds)))
            .Select(
                (issueId, index) => DomainGuard.RequiredIdentifier(
                    issueId,
                    $"{nameof(issueIds)}[{index}]"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalizedIssueIds.Length == 0)
        {
            throw new ArgumentException(
                "A repair-plan directive must cover at least one issue.");
        }

        Decision = decision;
        Risk = risk;
        Confidence = confidence;
        IssueIds = new ValueList<string>(normalizedIssueIds);
        RequiresUserConfirmation = requiresUserConfirmation;
        if (level == ModificationLevel.Safe && requiresUserConfirmation)
        {
            throw new ArgumentException(
                "A Safe directive cannot require user confirmation.",
                nameof(level));
        }

        if (level == ModificationLevel.Advisory
            && decision == RepairPlanDecision.Apply)
        {
            throw new ArgumentException(
                "An Advisory directive cannot be executable.",
                nameof(level));
        }

        if (level == ModificationLevel.Experimental
            && decision == RepairPlanDecision.Apply)
        {
            throw new ArgumentException(
                "An Experimental directive cannot be executable.",
                nameof(level));
        }

        Level = level;
        DependsOnScopeIds = new ValueList<string>(
            (dependsOnScopeIds ?? Array.Empty<string>())
            .Select(
                (scopeId, index) => DomainGuard.RequiredIdentifier(
                    scopeId,
                    $"{nameof(dependsOnScopeIds)}[{index}]"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));
        if (DependsOnScopeIds.Contains(
                ScopeId,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "A directive cannot depend on its own scope.",
                nameof(dependsOnScopeIds));
        }

        RollbackStrategy = DomainGuard.RequiredIdentifier(
            rollbackStrategy,
            nameof(rollbackStrategy));
        Reason = DomainGuard.RequiredIdentifier(reason, nameof(reason));
        SafetyNotes = new ValueList<string>(
            (safetyNotes ?? Array.Empty<string>())
            .Select(
                (note, index) => DomainGuard.RequiredIdentifier(
                    note,
                    $"{nameof(safetyNotes)}[{index}]"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));
    }

    public string DirectiveId { get; }

    public string ScopeId { get; }

    public string Scope { get; }

    public string RuleId { get; }

    public RepairPlanDecision Decision { get; }

    public RepairPlanRisk Risk { get; }

    public decimal Confidence { get; }

    public ValueList<string> IssueIds { get; }

    public int IssueCount => IssueIds.Count;

    public bool RequiresUserConfirmation { get; }

    public ModificationLevel Level { get; }

    public ValueList<string> DependsOnScopeIds { get; }

    public string RollbackStrategy { get; }

    public string Reason { get; }

    public ValueList<string> SafetyNotes { get; }
}

/// <summary>
/// Validated, content-safe plan produced before any DOCX mutation.
/// </summary>
public sealed record RepairPlan
{
    public const string CurrentSchemaVersion = "2.0";

    public RepairPlan(
        string planId,
        string sourceReportId,
        string sourceSha256,
        RepairPlanOrigin origin,
        string providerId,
        string model,
        bool visualEvidenceUsed,
        bool externalProcessingConsent,
        IEnumerable<RepairPlanDirective> directives,
        IEnumerable<string>? notices = null,
        string schemaVersion = CurrentSchemaVersion,
        IEnumerable<LayoutOperation>? layoutOperations = null)
    {
        SchemaVersion = DomainGuard.RequiredIdentifier(
            schemaVersion,
            nameof(schemaVersion));
        PlanId = DomainGuard.RequiredIdentifier(planId, nameof(planId));
        SourceReportId = DomainGuard.RequiredIdentifier(
            sourceReportId,
            nameof(sourceReportId));
        SourceSha256 = RequiredSha256(
            sourceSha256,
            nameof(sourceSha256));
        Origin = origin;
        ProviderId = DomainGuard.RequiredIdentifier(
            providerId,
            nameof(providerId));
        Model = DomainGuard.RequiredIdentifier(model, nameof(model));
        VisualEvidenceUsed = visualEvidenceUsed;
        ExternalProcessingConsent = externalProcessingConsent;

        RepairPlanDirective[] ordered = (
            directives ?? throw new ArgumentNullException(nameof(directives)))
            .OrderBy(item => item.RuleId, StringComparer.Ordinal)
            .ThenBy(item => item.ScopeId, StringComparer.Ordinal)
            .ThenBy(item => item.DirectiveId, StringComparer.Ordinal)
            .ToArray();
        if (ordered
            .GroupBy(item => item.DirectiveId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A repair plan cannot contain duplicate directive identifiers.",
                nameof(directives));
        }

        if (ordered
            .GroupBy(item => item.ScopeId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A repair plan must contain at most one directive per scope.",
                nameof(directives));
        }

        Directives = new ValueList<RepairPlanDirective>(ordered);
        Dictionary<string, RepairPlanDirective> byScope = ordered
            .ToDictionary(item => item.ScopeId, StringComparer.Ordinal);
        string? unknownDependency = ordered
            .SelectMany(item => item.DependsOnScopeIds)
            .FirstOrDefault(scopeId => !byScope.ContainsKey(scopeId));
        if (unknownDependency is not null)
        {
            throw new ArgumentException(
                $"Repair-plan dependency scope '{unknownDependency}' does not exist.",
                nameof(directives));
        }

        var resolved = new HashSet<string>(StringComparer.Ordinal);
        var execution = new List<string>();
        while (execution.Count < ordered.Length)
        {
            RepairPlanDirective[] ready = ordered
                .Where(item => !resolved.Contains(item.ScopeId))
                .Where(
                    item => item.DependsOnScopeIds.All(
                        resolved.Contains))
                .OrderBy(item => item.RuleId, StringComparer.Ordinal)
                .ThenBy(item => item.ScopeId, StringComparer.Ordinal)
                .ToArray();
            if (ready.Length == 0)
            {
                throw new ArgumentException(
                    "The repair plan contains a dependency cycle.",
                    nameof(directives));
            }

            foreach (RepairPlanDirective item in ready)
            {
                resolved.Add(item.ScopeId);
                execution.Add(item.DirectiveId);
            }
        }

        ExecutionOrder = new ValueList<string>(execution);
        LayoutOperation[] layout = (
            layoutOperations ?? Array.Empty<LayoutOperation>())
            .OrderBy(item => item.OperationId, StringComparer.Ordinal)
            .ToArray();
        if (layout
            .GroupBy(item => item.OperationId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A repair plan cannot contain duplicate layout operation identifiers.",
                nameof(layoutOperations));
        }

        Dictionary<string, LayoutOperation> layoutById = layout
            .ToDictionary(item => item.OperationId, StringComparer.Ordinal);
        string? unknownLayoutDependency = layout
            .SelectMany(item => item.DependsOnOperationIds)
            .FirstOrDefault(id => !layoutById.ContainsKey(id));
        if (unknownLayoutDependency is not null)
        {
            throw new ArgumentException(
                $"Layout dependency '{unknownLayoutDependency}' does not exist.",
                nameof(layoutOperations));
        }

        var layoutResolved = new HashSet<string>(StringComparer.Ordinal);
        var layoutOrder = new List<string>();
        while (layoutOrder.Count < layout.Length)
        {
            LayoutOperation[] ready = layout
                .Where(item => !layoutResolved.Contains(item.OperationId))
                .Where(
                    item => item.DependsOnOperationIds.All(
                        layoutResolved.Contains))
                .OrderBy(item => item.OperationId, StringComparer.Ordinal)
                .ToArray();
            if (ready.Length == 0)
            {
                throw new ArgumentException(
                    "The repair plan contains a layout dependency cycle.",
                    nameof(layoutOperations));
            }

            foreach (LayoutOperation item in ready)
            {
                layoutResolved.Add(item.OperationId);
                layoutOrder.Add(item.OperationId);
            }
        }

        LayoutOperations = new ValueList<LayoutOperation>(layout);
        LayoutExecutionOrder = new ValueList<string>(layoutOrder);
        Notices = new ValueList<string>(
            (notices ?? Array.Empty<string>())
            .Select(
                (notice, index) => DomainGuard.RequiredIdentifier(
                    notice,
                    $"{nameof(notices)}[{index}]"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));
    }

    public string SchemaVersion { get; }

    public string PlanId { get; }

    public string SourceReportId { get; }

    public string SourceSha256 { get; }

    public RepairPlanOrigin Origin { get; }

    public string ProviderId { get; }

    public string Model { get; }

    public bool VisualEvidenceUsed { get; }

    public bool ExternalProcessingConsent { get; }

    public ValueList<RepairPlanDirective> Directives { get; }

    public ValueList<string> ExecutionOrder { get; }

    public ValueList<LayoutOperation> LayoutOperations { get; }

    public ValueList<string> LayoutExecutionOrder { get; }

    public ValueList<string> Notices { get; }

    private static string RequiredSha256(
        string value,
        string parameterName)
    {
        string normalized = DomainGuard.RequiredIdentifier(
            value,
            parameterName);
        if (normalized.Length != 64
            || normalized.Any(
                character => character is not
                    (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "A source SHA-256 must contain 64 lowercase hexadecimal characters.",
                parameterName);
        }

        return normalized;
    }
}

/// <summary>
/// State of the rendered-page review after a deterministic repair.
/// </summary>
public enum VisualReviewStatus
{
    NotRun,
    Passed,
    NeedsReview,
    Failed,
}

/// <summary>
/// One content-safe rendered-page finding.
/// </summary>
public sealed record VisualReviewFinding
{
    public VisualReviewFinding(
        string code,
        RepairPlanRisk risk,
        string message,
        int? sourcePage = null,
        int? outputPage = null)
    {
        Code = DomainGuard.RequiredIdentifier(code, nameof(code));
        Risk = risk;
        Message = DomainGuard.RequiredIdentifier(message, nameof(message));
        if (sourcePage is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePage));
        }

        if (outputPage is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputPage));
        }

        SourcePage = sourcePage;
        OutputPage = outputPage;
    }

    public string Code { get; }

    public RepairPlanRisk Risk { get; }

    public string Message { get; }

    public int? SourcePage { get; }

    public int? OutputPage { get; }
}

/// <summary>
/// Rendered-page verification result. It contains no manuscript body text.
/// </summary>
public sealed record VisualReviewReport
{
    public VisualReviewReport(
        VisualReviewStatus status,
        string providerId,
        string model,
        int sourcePageCount,
        int outputPageCount,
        IEnumerable<VisualReviewFinding>? findings = null,
        string? summary = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourcePageCount);
        ArgumentOutOfRangeException.ThrowIfNegative(outputPageCount);

        Status = status;
        ProviderId = DomainGuard.RequiredIdentifier(
            providerId,
            nameof(providerId));
        Model = DomainGuard.RequiredIdentifier(model, nameof(model));
        SourcePageCount = sourcePageCount;
        OutputPageCount = outputPageCount;
        Findings = new ValueList<VisualReviewFinding>(
            (findings ?? Array.Empty<VisualReviewFinding>())
            .OrderBy(item => item.Risk)
            .ThenBy(item => item.Code, StringComparer.Ordinal));
        Summary = DomainGuard.OptionalNonBlank(summary, nameof(summary));
    }

    public VisualReviewStatus Status { get; }

    public string ProviderId { get; }

    public string Model { get; }

    public int SourcePageCount { get; }

    public int OutputPageCount { get; }

    public ValueList<VisualReviewFinding> Findings { get; }

    public string? Summary { get; }

    public static VisualReviewReport NotRun(string reason) =>
        new(
            VisualReviewStatus.NotRun,
            "none",
            "none",
            0,
            0,
            [
                new VisualReviewFinding(
                    "visual_review.not_run",
                    RepairPlanRisk.High,
                    reason),
            ],
            reason);
}
