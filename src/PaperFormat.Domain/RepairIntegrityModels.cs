namespace PaperFormat.Domain;

/// <summary>
/// How a deterministic repair was authorized.
/// </summary>
public enum RepairAuthorization
{
    SafeAutomatic,
    UserConfirmed,
}

/// <summary>
/// Result of one requested repair operation.
/// </summary>
public enum RepairExecutionStatus
{
    Applied,
    Skipped,
    Failed,
}

/// <summary>
/// Overall content-integrity state.
/// </summary>
public enum IntegrityStatus
{
    Passed,
    NeedsConfirmation,
    Failed,
}

/// <summary>
/// A selection of issue identifiers approved for repair.
/// </summary>
public sealed record RepairSelection
{
    public RepairSelection(
        IEnumerable<string> issueIds,
        bool pageChangesConfirmed = false,
        IEnumerable<string>? userConfirmedIssueIds = null)
    {
        ArgumentNullException.ThrowIfNull(issueIds);
        string[] values = issueIds
            .Select(
                (issueId, index) => DomainGuard.RequiredIdentifier(
                    issueId,
                    $"{nameof(issueIds)}[{index}]"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new ArgumentException(
                "A repair selection cannot contain duplicate issue identifiers.",
                nameof(issueIds));
        }

        IssueIds = new ValueList<string>(values);
        PageChangesConfirmed = pageChangesConfirmed;
        string[] confirmed = (
            userConfirmedIssueIds ?? Array.Empty<string>())
            .Select(
                (issueId, index) => DomainGuard.RequiredIdentifier(
                    issueId,
                    $"{nameof(userConfirmedIssueIds)}[{index}]"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (confirmed.Any(issueId => !values.Contains(
                issueId,
                StringComparer.Ordinal)))
        {
            throw new ArgumentException(
                "User-confirmed issues must be included in the repair selection.",
                nameof(userConfirmedIssueIds));
        }

        UserConfirmedIssueIds = new ValueList<string>(confirmed);
    }

    public ValueList<string> IssueIds { get; }

    public bool PageChangesConfirmed { get; }

    public ValueList<string> UserConfirmedIssueIds { get; }
}

/// <summary>
/// One auditable repair attempt.
/// </summary>
public sealed record ChangeLogEntry
{
    public ChangeLogEntry(
        string changeId,
        string issueId,
        string ruleId,
        RuleTarget elementType,
        StructuralLocation documentLocation,
        FormatProperty property,
        RuleValue? originalValue,
        RuleValue targetValue,
        RuleEvidence ruleSource,
        RepairAuthorization authorization,
        RepairExecutionStatus status,
        string message)
    {
        ChangeId = DomainGuard.RequiredIdentifier(changeId, nameof(changeId));
        IssueId = DomainGuard.RequiredIdentifier(issueId, nameof(issueId));
        RuleId = DomainGuard.RequiredIdentifier(ruleId, nameof(ruleId));
        ElementType = elementType;
        DocumentLocation = documentLocation
            ?? throw new ArgumentNullException(nameof(documentLocation));
        Property = property;
        OriginalValue = originalValue;
        TargetValue = targetValue
            ?? throw new ArgumentNullException(nameof(targetValue));
        RuleSource = ruleSource
            ?? throw new ArgumentNullException(nameof(ruleSource));
        Authorization = authorization;
        Status = status;
        Message = DomainGuard.RequiredIdentifier(message, nameof(message));
    }

    public string ChangeId { get; }

    public string IssueId { get; }

    public string RuleId { get; }

    public RuleTarget ElementType { get; }

    public StructuralLocation DocumentLocation { get; }

    public FormatProperty Property { get; }

    public RuleValue? OriginalValue { get; }

    public RuleValue TargetValue { get; }

    public RuleEvidence RuleSource { get; }

    public RepairAuthorization Authorization { get; }

    public RepairExecutionStatus Status { get; }

    public string Message { get; }
}

/// <summary>
/// Versioned audit log for one immutable-source repair execution.
/// </summary>
public sealed record ChangeLog
{
    public const string CurrentSchemaVersion = "1.0";

    public ChangeLog(
        string operationId,
        string sourceSha256,
        string outputSha256,
        bool originalPreserved,
        bool outputReopened,
        bool packageValid,
        IEnumerable<ChangeLogEntry> entries,
        string schemaVersion = CurrentSchemaVersion)
    {
        SchemaVersion = DomainGuard.RequiredIdentifier(
            schemaVersion,
            nameof(schemaVersion));
        OperationId = DomainGuard.RequiredIdentifier(
            operationId,
            nameof(operationId));
        SourceSha256 = RequiredSha256(sourceSha256, nameof(sourceSha256));
        OutputSha256 = RequiredSha256(outputSha256, nameof(outputSha256));
        OriginalPreserved = originalPreserved;
        OutputReopened = outputReopened;
        PackageValid = packageValid;
        Entries = new ValueList<ChangeLogEntry>(
            entries ?? throw new ArgumentNullException(nameof(entries)));
    }

    public string SchemaVersion { get; }

    public string OperationId { get; }

    public string SourceSha256 { get; }

    public string OutputSha256 { get; }

    public bool OriginalPreserved { get; }

    public bool OutputReopened { get; }

    public bool PackageValid { get; }

    public ValueList<ChangeLogEntry> Entries { get; }

    private static string RequiredSha256(string value, string parameterName)
    {
        string normalized =
            DomainGuard.RequiredIdentifier(value, parameterName);
        if (normalized.Length != 64
            || normalized.Any(
                character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A SHA-256 value must contain exactly 64 hexadecimal characters.",
                parameterName);
        }

        return normalized.ToLowerInvariant();
    }
}

/// <summary>
/// Stable identifiers used by integrity policies and reports.
/// </summary>
public static class IntegrityCheckIds
{
    public const string SectionTopology = "section_topology";
}

/// <summary>
/// One content-bearing feature comparison.
/// </summary>
public sealed record IntegrityCheck
{
    public IntegrityCheck(
        string checkId,
        IntegrityStatus status,
        int sourceCount,
        int outputCount,
        string sourceSha256,
        string outputSha256,
        string message)
    {
        CheckId = DomainGuard.RequiredIdentifier(checkId, nameof(checkId));
        Status = status;
        DomainGuard.NonNegative(sourceCount, nameof(sourceCount));
        DomainGuard.NonNegative(outputCount, nameof(outputCount));
        SourceCount = sourceCount;
        OutputCount = outputCount;
        SourceSha256 = DomainGuard.RequiredIdentifier(
            sourceSha256,
            nameof(sourceSha256));
        OutputSha256 = DomainGuard.RequiredIdentifier(
            outputSha256,
            nameof(outputSha256));
        Message = DomainGuard.RequiredIdentifier(message, nameof(message));
    }

    public string CheckId { get; }

    public IntegrityStatus Status { get; }

    public int SourceCount { get; }

    public int OutputCount { get; }

    public string SourceSha256 { get; }

    public string OutputSha256 { get; }

    public string Message { get; }
}

/// <summary>
/// Content-integrity comparison that intentionally contains no manuscript text.
/// </summary>
public sealed record IntegrityReport
{
    public const string CurrentSchemaVersion = "1.0";

    public IntegrityReport(
        string reportId,
        IntegrityStatus status,
        IEnumerable<IntegrityCheck> checks,
        string schemaVersion = CurrentSchemaVersion)
    {
        SchemaVersion = DomainGuard.RequiredIdentifier(
            schemaVersion,
            nameof(schemaVersion));
        ReportId = DomainGuard.RequiredIdentifier(reportId, nameof(reportId));
        Status = status;
        Checks = new ValueList<IntegrityCheck>(
            (checks ?? throw new ArgumentNullException(nameof(checks)))
            .OrderBy(check => check.CheckId, StringComparer.Ordinal));
        IntegrityStatus derived = Checks.Any(
            check => check.Status == IntegrityStatus.Failed)
            ? IntegrityStatus.Failed
            : Checks.Any(
                check => check.Status == IntegrityStatus.NeedsConfirmation)
                ? IntegrityStatus.NeedsConfirmation
                : IntegrityStatus.Passed;
        if (derived != status)
        {
            throw new ArgumentException(
                "The integrity status must match the most severe check status.",
                nameof(status));
        }
    }

    public string SchemaVersion { get; }

    public string ReportId { get; }

    public IntegrityStatus Status { get; }

    public ValueList<IntegrityCheck> Checks { get; }
}

/// <summary>
/// Complete result of repairing, reopening, rechecking, and validating output.
/// </summary>
public sealed record RepairResult(
    ChangeLog ChangeLog,
    IntegrityReport Integrity,
    CheckReport PostRepairCheck,
    bool IsReadyForUse,
    VisualReviewReport? VisualReview = null);
