using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaperFormat.Domain;

namespace PaperFormat.Ai;

public static class RepairPlanJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(RepairPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan, Options);
    }

    public static RepairPlan Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        RepairPlanContract contract =
            JsonSerializer.Deserialize<RepairPlanContract>(json, Options)
            ?? throw new JsonException("The repair-plan JSON was empty.");
        return new RepairPlan(
            contract.PlanId,
            contract.SourceReportId,
            contract.SourceSha256,
            contract.Origin,
            contract.ProviderId,
            contract.Model,
            contract.VisualEvidenceUsed,
            contract.ExternalProcessingConsent,
            contract.Directives.Select(
                directive => new RepairPlanDirective(
                    directive.DirectiveId,
                    directive.ScopeId,
                    directive.Scope,
                    directive.RuleId,
                    directive.Decision,
                    directive.Risk,
                    directive.Confidence,
                    directive.IssueIds,
                    directive.RequiresUserConfirmation,
                    directive.Reason,
                    directive.SafetyNotes,
                    directive.Level,
                    directive.DependsOnScopeIds,
                    directive.RollbackStrategy)),
            contract.Notices,
            contract.SchemaVersion,
            contract.LayoutOperations.Select(
                operation => new LayoutOperation(
                    operation.OperationId,
                    operation.Kind,
                    operation.Decision,
                    operation.Risk,
                    operation.Level,
                    operation.RequiresUserConfirmation,
                    operation.Reason,
                    operation.RollbackStrategy,
                    operation.DependsOnOperationIds,
                    operation.AfterElementId,
                    operation.TargetSectionIndex,
                    operation.ColumnCount,
                    operation.ColumnSpacingTwips,
                    operation.ObjectElementId,
                    operation.Strategy)));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = true,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed record RepairPlanContract
    {
        public required string SchemaVersion { get; init; }

        public required string PlanId { get; init; }

        public required string SourceReportId { get; init; }

        public required string SourceSha256 { get; init; }

        public required RepairPlanOrigin Origin { get; init; }

        public required string ProviderId { get; init; }

        public required string Model { get; init; }

        public required bool VisualEvidenceUsed { get; init; }

        public required bool ExternalProcessingConsent { get; init; }

        public required RepairPlanDirectiveContract[] Directives { get; init; }

        public required LayoutOperationContract[] LayoutOperations { get; init; }

        public required string[] Notices { get; init; }
    }

    private sealed record RepairPlanDirectiveContract
    {
        public required string DirectiveId { get; init; }

        public required string ScopeId { get; init; }

        public required string Scope { get; init; }

        public required string RuleId { get; init; }

        public required RepairPlanDecision Decision { get; init; }

        public required RepairPlanRisk Risk { get; init; }

        public required decimal Confidence { get; init; }

        public required string[] IssueIds { get; init; }

        public required bool RequiresUserConfirmation { get; init; }

        public required ModificationLevel Level { get; init; }

        public required string[] DependsOnScopeIds { get; init; }

        public required string RollbackStrategy { get; init; }

        public required string Reason { get; init; }

        public required string[] SafetyNotes { get; init; }
    }

    private sealed record LayoutOperationContract
    {
        public required string OperationId { get; init; }

        public required LayoutOperationKind Kind { get; init; }

        public required RepairPlanDecision Decision { get; init; }

        public required RepairPlanRisk Risk { get; init; }

        public required ModificationLevel Level { get; init; }

        public required bool RequiresUserConfirmation { get; init; }

        public required string Reason { get; init; }

        public required string RollbackStrategy { get; init; }

        public required string[] DependsOnOperationIds { get; init; }

        public string? AfterElementId { get; init; }

        public int? TargetSectionIndex { get; init; }

        public int? ColumnCount { get; init; }

        public int? ColumnSpacingTwips { get; init; }

        public string? ObjectElementId { get; init; }

        public string? Strategy { get; init; }
    }
}
