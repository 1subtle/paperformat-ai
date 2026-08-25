namespace PaperFormat.Domain;

/// <summary>
/// Overall result of a deterministic format check.
/// </summary>
public enum CheckStatus
{
    Passed,
    IssuesFound,
    NeedsConfirmation,
}

/// <summary>
/// A skipped rule and the deterministic reason it was not evaluated.
/// </summary>
public sealed record SkippedRule
{
    public SkippedRule(string ruleId, string reasonCode, string message)
    {
        RuleId = DomainGuard.RequiredIdentifier(ruleId, nameof(ruleId));
        ReasonCode = DomainGuard.RequiredIdentifier(
            reasonCode,
            nameof(reasonCode));
        Message = DomainGuard.RequiredIdentifier(message, nameof(message));
    }

    public string RuleId { get; }

    public string ReasonCode { get; }

    public string Message { get; }
}

/// <summary>
/// A classification that must be confirmed before applying target rules.
/// </summary>
public sealed record PendingElement
{
    public PendingElement(
        string elementId,
        StructuralLocation location,
        ManuscriptElementKind proposedKind,
        decimal confidence)
    {
        ElementId = DomainGuard.RequiredIdentifier(
            elementId,
            nameof(elementId));
        Location = location ?? throw new ArgumentNullException(nameof(location));
        if (proposedKind == ManuscriptElementKind.Unclassified)
        {
            throw new ArgumentException(
                "A pending element requires a proposed element kind.",
                nameof(proposedKind));
        }

        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                confidence,
                "Pending element confidence must be between zero and one.");
        }

        ProposedKind = proposedKind;
        Confidence = confidence;
    }

    public string ElementId { get; }

    public StructuralLocation Location { get; }

    public ManuscriptElementKind ProposedKind { get; }

    public decimal Confidence { get; }
}

/// <summary>
/// One concrete mismatch between a rule and a document location.
/// </summary>
public sealed record CheckIssue
{
    public CheckIssue(
        string issueId,
        string ruleId,
        RuleSeverity severity,
        RuleTarget elementType,
        StructuralLocation documentLocation,
        RuleValue? currentValue,
        RuleValue expectedValue,
        string message,
        RuleEvidence ruleSource,
        decimal confidence,
        bool autoFixable)
    {
        IssueId = DomainGuard.RequiredIdentifier(issueId, nameof(issueId));
        RuleId = DomainGuard.RequiredIdentifier(ruleId, nameof(ruleId));
        Severity = severity;
        ElementType = elementType;
        DocumentLocation = documentLocation
            ?? throw new ArgumentNullException(nameof(documentLocation));
        CurrentValue = currentValue;
        ExpectedValue = expectedValue
            ?? throw new ArgumentNullException(nameof(expectedValue));
        Message = DomainGuard.RequiredIdentifier(message, nameof(message));
        RuleSource = ruleSource
            ?? throw new ArgumentNullException(nameof(ruleSource));
        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                confidence,
                "Issue confidence must be between zero and one.");
        }

        Confidence = confidence;
        AutoFixable = autoFixable;
    }

    public string IssueId { get; }

    public string RuleId { get; }

    public RuleSeverity Severity { get; }

    public RuleTarget ElementType { get; }

    public StructuralLocation DocumentLocation { get; }

    public RuleValue? CurrentValue { get; }

    public RuleValue ExpectedValue { get; }

    public string Message { get; }

    public RuleEvidence RuleSource { get; }

    public decimal Confidence { get; }

    public bool AutoFixable { get; }
}

/// <summary>
/// Aggregate counts and score for a format check.
/// </summary>
public sealed record CheckSummary
{
    public CheckSummary(
        int enabledRules,
        int evaluatedObservations,
        int passedObservations,
        int issueCount,
        int errorCount,
        int warningCount,
        int informationCount,
        int skippedRuleCount,
        int pendingElementCount,
        int score)
    {
        ValidateNonNegative(enabledRules, nameof(enabledRules));
        ValidateNonNegative(
            evaluatedObservations,
            nameof(evaluatedObservations));
        ValidateNonNegative(passedObservations, nameof(passedObservations));
        ValidateNonNegative(issueCount, nameof(issueCount));
        ValidateNonNegative(errorCount, nameof(errorCount));
        ValidateNonNegative(warningCount, nameof(warningCount));
        ValidateNonNegative(informationCount, nameof(informationCount));
        ValidateNonNegative(skippedRuleCount, nameof(skippedRuleCount));
        ValidateNonNegative(pendingElementCount, nameof(pendingElementCount));
        if (passedObservations > evaluatedObservations)
        {
            throw new ArgumentException(
                "Passed observations cannot exceed evaluated observations.");
        }

        if (issueCount != errorCount + warningCount + informationCount)
        {
            throw new ArgumentException(
                "Issue severity counts must add up to the issue count.");
        }

        if (score is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(score),
                score,
                "A format score must be between zero and one hundred.");
        }

        EnabledRules = enabledRules;
        EvaluatedObservations = evaluatedObservations;
        PassedObservations = passedObservations;
        IssueCount = issueCount;
        ErrorCount = errorCount;
        WarningCount = warningCount;
        InformationCount = informationCount;
        SkippedRuleCount = skippedRuleCount;
        PendingElementCount = pendingElementCount;
        Score = score;
    }

    public int EnabledRules { get; }

    public int EvaluatedObservations { get; }

    public int PassedObservations { get; }

    public int IssueCount { get; }

    public int ErrorCount { get; }

    public int WarningCount { get; }

    public int InformationCount { get; }

    public int SkippedRuleCount { get; }

    public int PendingElementCount { get; }

    public int Score { get; }

    private static void ValidateNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A check summary count cannot be negative.");
        }
    }
}

/// <summary>
/// Versioned deterministic result of checking one document against one package.
/// </summary>
public sealed record CheckReport
{
    public const string CurrentSchemaVersion = "1.0";

    public CheckReport(
        string reportId,
        string rulePackageId,
        int rulePackageRevision,
        CheckStatus status,
        CheckSummary summary,
        IEnumerable<CheckIssue> issues,
        IEnumerable<SkippedRule> skippedRules,
        IEnumerable<PendingElement> pendingElements,
        string schemaVersion = CurrentSchemaVersion)
    {
        ReportId = DomainGuard.RequiredIdentifier(reportId, nameof(reportId));
        RulePackageId = DomainGuard.RequiredIdentifier(
            rulePackageId,
            nameof(rulePackageId));
        if (rulePackageRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rulePackageRevision),
                rulePackageRevision,
                "A referenced rule package revision must be positive.");
        }

        SchemaVersion = DomainGuard.RequiredIdentifier(
            schemaVersion,
            nameof(schemaVersion));
        RulePackageRevision = rulePackageRevision;
        Status = status;
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        Issues = new ValueList<CheckIssue>(
            issues ?? throw new ArgumentNullException(nameof(issues)));
        SkippedRules = new ValueList<SkippedRule>(
            skippedRules ?? throw new ArgumentNullException(nameof(skippedRules)));
        PendingElements = new ValueList<PendingElement>(
            pendingElements
            ?? throw new ArgumentNullException(nameof(pendingElements)));
    }

    public string SchemaVersion { get; }

    public string ReportId { get; }

    public string RulePackageId { get; }

    public int RulePackageRevision { get; }

    public CheckStatus Status { get; }

    public CheckSummary Summary { get; }

    public ValueList<CheckIssue> Issues { get; }

    public ValueList<SkippedRule> SkippedRules { get; }

    public ValueList<PendingElement> PendingElements { get; }
}
