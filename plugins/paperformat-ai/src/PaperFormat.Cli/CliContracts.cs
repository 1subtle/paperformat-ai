using PaperFormat.Domain;
using PaperFormat.Rendering;

namespace PaperFormat.Cli;

public static class CliExitCodes
{
    public const int Success = 0;
    public const int InvalidInput = 2;
    public const int NeedsConfirmation = 3;
    public const int ValidationFailed = 4;
    public const int ToolUnavailable = 5;
    public const int UnexpectedFailure = 10;
}

public sealed record CliResult(
    string SchemaVersion,
    string Command,
    string Status,
    object? Data,
    IReadOnlyList<CliDiagnostic> Diagnostics,
    IReadOnlyList<string> NextActions)
{
    public const string CurrentSchemaVersion = "1.0";
}

public sealed record CliDiagnostic(
    string Code,
    string Severity,
    string Message);

public sealed record RenderManifest(
    string SchemaVersion,
    string SourceSha256,
    string Renderer,
    int Dpi,
    string Pdf,
    IReadOnlyList<RenderPageManifest> Pages)
{
    public const string CurrentSchemaVersion = "1.0";
}

public sealed record RenderPageManifest(
    int PageNumber,
    string File,
    string Sha256,
    int Width,
    int Height);

public sealed record ValidationReport(
    string SchemaVersion,
    string Status,
    bool SourcePreserved,
    bool CandidateReopened,
    IntegrityReport Integrity,
    PageComparisonReport? PageComparison,
    IReadOnlyList<string> BlockingReasons,
    VisualReviewReport? VisualReview = null,
    string? PlanId = null,
    string? OperationId = null)
{
    public const string CurrentSchemaVersion = "1.0";
}

public sealed record ExternalVisualReviewSubmission(
    string SchemaVersion,
    string PlanId,
    string OperationId,
    VisualReviewStatus Status,
    string ProviderId,
    string Model,
    int SourcePageCount,
    int OutputPageCount,
    IReadOnlyList<VisualReviewFinding> Findings,
    string? Summary)
{
    public const string CurrentSchemaVersion = "1.0";
}

public sealed record ValidatedVisualReview(
    string SchemaVersion,
    string PlanId,
    string OperationId,
    string SourceSha256,
    string OutputSha256,
    bool EvidenceBound,
    VisualReviewReport Review)
{
    public const string CurrentSchemaVersion = "1.0";
}

public sealed record WorkflowManifest(
    string SchemaVersion,
    string TaskId,
    string Status,
    string SourceSha256,
    string FormatSource,
    IReadOnlyDictionary<string, string> Artifacts,
    IReadOnlyList<string> NextActions)
{
    public const string CurrentSchemaVersion = "1.0";
}

public sealed record ApplyManifest(
    string SchemaVersion,
    string Status,
    string PlanId,
    string OperationId,
    string SourceSha256,
    string OutputSha256,
    int SafeDirectiveCount,
    int ReviewDirectiveCount,
    int ExperimentalDirectiveCount,
    int AppliedChangeCount,
    int AppliedLayoutOperationCount,
    bool OriginalPreserved,
    bool OutputReopened,
    bool PackageValid,
    string IntegrityStatus,
    string PostCheckStatus,
    bool ReadyForVisualValidation,
    IReadOnlyDictionary<string, string> Artifacts)
{
    public const string CurrentSchemaVersion = "1.0";
}

public sealed record ExperimentalAttemptManifest(
    string SchemaVersion,
    string Status,
    string AttemptId,
    string PlanId,
    string SourceReportId,
    string SourceSha256,
    string CandidateSha256,
    IReadOnlyList<string> SelectedExperimentalIds,
    bool OriginalPreserved,
    bool CandidateReopened,
    bool ReadyForUse,
    IReadOnlyDictionary<string, string> Artifacts,
    IReadOnlyList<string> NextActions)
{
    public const string CurrentSchemaVersion = "1.0";
}

public sealed record ExportManifest(
    string SchemaVersion,
    string Status,
    string SourceSha256,
    string OutputSha256,
    IReadOnlyDictionary<string, string> Artifacts,
    IReadOnlyList<string> RemainingGates)
{
    public const string CurrentSchemaVersion = "1.0";
}
