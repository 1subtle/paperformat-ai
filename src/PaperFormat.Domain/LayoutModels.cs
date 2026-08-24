namespace PaperFormat.Domain;

public enum LayoutOperationKind
{
    InsertContinuousSectionBreak,
    InsertNextPageSectionBreak,
    SetSectionColumns,
    PreserveFullWidthObject,
}

public enum LayoutRiskObjectKind
{
    WideTable,
    MergedTable,
    InlineDrawing,
    FloatingDrawing,
    Equation,
    Field,
}

public sealed record LayoutRiskFinding(
    string FindingId,
    LayoutRiskObjectKind Kind,
    ModificationLevel Level,
    StructuralLocation Location,
    string Message);

public sealed record LayoutAnalysis
{
    public const string CurrentSchemaVersion = "1.0";

    public LayoutAnalysis(
        string sourceSha256,
        int sourceSectionCount,
        IEnumerable<int> sourceColumnCounts,
        int targetColumnCount,
        int targetColumnSpacingTwips,
        string? frontMatterEndElementId,
        string? bodyStartElementId,
        bool canConvert,
        IEnumerable<string> blockers,
        IEnumerable<LayoutRiskFinding> risks,
        string schemaVersion = CurrentSchemaVersion)
    {
        SchemaVersion = Required(schemaVersion, nameof(schemaVersion));
        SourceSha256 = Required(sourceSha256, nameof(sourceSha256));
        ArgumentOutOfRangeException.ThrowIfNegative(sourceSectionCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            targetColumnCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            targetColumnSpacingTwips);
        SourceSectionCount = sourceSectionCount;
        SourceColumnCounts = new ValueList<int>(
            sourceColumnCounts
                ?? throw new ArgumentNullException(nameof(sourceColumnCounts)));
        TargetColumnCount = targetColumnCount;
        TargetColumnSpacingTwips = targetColumnSpacingTwips;
        FrontMatterEndElementId = Optional(
            frontMatterEndElementId,
            nameof(frontMatterEndElementId));
        BodyStartElementId = Optional(
            bodyStartElementId,
            nameof(bodyStartElementId));
        CanConvert = canConvert;
        Blockers = new ValueList<string>(
            (blockers ?? throw new ArgumentNullException(nameof(blockers)))
            .Select(
                (item, index) => Required(
                    item,
                    $"{nameof(blockers)}[{index}]")));
        Risks = new ValueList<LayoutRiskFinding>(
            risks ?? throw new ArgumentNullException(nameof(risks)));
    }

    public string SchemaVersion { get; }
    public string SourceSha256 { get; }
    public int SourceSectionCount { get; }
    public ValueList<int> SourceColumnCounts { get; }
    public int TargetColumnCount { get; }
    public int TargetColumnSpacingTwips { get; }
    public string? FrontMatterEndElementId { get; }
    public string? BodyStartElementId { get; }
    public bool CanConvert { get; }
    public ValueList<string> Blockers { get; }
    public ValueList<LayoutRiskFinding> Risks { get; }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static string? Optional(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        return Required(value, parameterName);
    }
}

public sealed record LayoutOperation
{
    public LayoutOperation(
        string operationId,
        LayoutOperationKind kind,
        RepairPlanDecision decision,
        RepairPlanRisk risk,
        ModificationLevel level,
        bool requiresUserConfirmation,
        string reason,
        string rollbackStrategy,
        IEnumerable<string>? dependsOnOperationIds = null,
        string? afterElementId = null,
        int? targetSectionIndex = null,
        int? columnCount = null,
        int? columnSpacingTwips = null,
        string? objectElementId = null,
        string? strategy = null)
    {
        OperationId = Required(operationId, nameof(operationId));
        Kind = kind;
        Decision = decision;
        Risk = risk;
        Level = level;
        RequiresUserConfirmation = requiresUserConfirmation;
        if (level == ModificationLevel.Safe)
        {
            throw new ArgumentException(
                "Structural layout operations cannot be Safe.",
                nameof(level));
        }

        if (level == ModificationLevel.Advisory
            && decision == RepairPlanDecision.Apply)
        {
            throw new ArgumentException(
                "Advisory layout operations cannot be executable.",
                nameof(level));
        }

        if (level == ModificationLevel.Experimental
            && decision == RepairPlanDecision.Apply)
        {
            throw new ArgumentException(
                "Experimental layout operations cannot be executable.",
                nameof(level));
        }

        Reason = Required(reason, nameof(reason));
        RollbackStrategy = Required(
            rollbackStrategy,
            nameof(rollbackStrategy));
        DependsOnOperationIds = new ValueList<string>(
            (dependsOnOperationIds ?? Array.Empty<string>())
            .Select(
                (item, index) => Required(
                    item,
                    $"{nameof(dependsOnOperationIds)}[{index}]"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));
        if (DependsOnOperationIds.Contains(
                OperationId,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "A layout operation cannot depend on itself.",
                nameof(dependsOnOperationIds));
        }

        AfterElementId = Optional(afterElementId, nameof(afterElementId));
        TargetSectionIndex = targetSectionIndex;
        ColumnCount = columnCount;
        ColumnSpacingTwips = columnSpacingTwips;
        ObjectElementId = Optional(
            objectElementId,
            nameof(objectElementId));
        Strategy = Optional(strategy, nameof(strategy));
        ValidateShape();
    }

    public string OperationId { get; }
    public LayoutOperationKind Kind { get; }
    public RepairPlanDecision Decision { get; }
    public RepairPlanRisk Risk { get; }
    public ModificationLevel Level { get; }
    public bool RequiresUserConfirmation { get; }
    public string Reason { get; }
    public string RollbackStrategy { get; }
    public ValueList<string> DependsOnOperationIds { get; }
    public string? AfterElementId { get; }
    public int? TargetSectionIndex { get; }
    public int? ColumnCount { get; }
    public int? ColumnSpacingTwips { get; }
    public string? ObjectElementId { get; }
    public string? Strategy { get; }

    private void ValidateShape()
    {
        if ((Kind is
                LayoutOperationKind.InsertContinuousSectionBreak
                or LayoutOperationKind.InsertNextPageSectionBreak)
            && AfterElementId is null)
        {
            throw new ArgumentException(
                "A continuous section break requires afterElementId.");
        }

        if (Kind == LayoutOperationKind.SetSectionColumns)
        {
            if (TargetSectionIndex is null or < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(TargetSectionIndex));
            }

            if (ColumnCount is null or < 1 or > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(ColumnCount));
            }

            if (ColumnSpacingTwips is null or < 0 or > 2_880)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ColumnSpacingTwips));
            }
        }

        if (Kind == LayoutOperationKind.PreserveFullWidthObject
            && (ObjectElementId is null || Strategy is null))
        {
            throw new ArgumentException(
                "Preserving a full-width object requires objectElementId and strategy.");
        }
    }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static string? Optional(string? value, string parameterName) =>
        value is null ? null : Required(value, parameterName);
}

public sealed record LayoutChangeEntry(
    string OperationId,
    LayoutOperationKind Kind,
    RepairExecutionStatus Status,
    string Message);

public sealed record LayoutChangeLog(
    string SchemaVersion,
    string SourceSha256,
    string OutputSha256,
    IReadOnlyList<LayoutChangeEntry> Entries)
{
    public const string CurrentSchemaVersion = "1.0";
}
