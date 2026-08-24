namespace PaperFormat.Domain;

/// <summary>
/// The OOXML package kind supplied to the parser.
/// </summary>
public enum WordPackageKind
{
    Document,
    Template,
}

/// <summary>
/// A Word style family.
/// </summary>
public enum StyleKind
{
    Unknown,
    Paragraph,
    Character,
    Table,
    Numbering,
}

/// <summary>
/// Effective document defaults used as the base of style resolution.
/// </summary>
public sealed record DocumentDefaults(
    ParagraphFormatting Paragraph,
    CharacterFormatting Character)
{
    public static DocumentDefaults Empty { get; } = new(
        ParagraphFormatting.Empty,
        CharacterFormatting.Empty);
}

/// <summary>
/// A style definition before or after inheritance resolution.
/// </summary>
public sealed record StyleDefinition
{
    public StyleDefinition(
        string styleId,
        string? name,
        StyleKind kind,
        string? basedOnStyleId,
        string? linkedStyleId,
        bool isDefault,
        bool isCustom,
        ParagraphFormatting paragraphFormatting,
        CharacterFormatting characterFormatting,
        TableFormatting tableFormatting)
    {
        StyleId = DomainGuard.RequiredIdentifier(styleId, nameof(styleId));
        Name = DomainGuard.OptionalNonBlank(name, nameof(name));
        Kind = kind;
        BasedOnStyleId = DomainGuard.OptionalNonBlank(
            basedOnStyleId,
            nameof(basedOnStyleId));
        LinkedStyleId = DomainGuard.OptionalNonBlank(
            linkedStyleId,
            nameof(linkedStyleId));
        IsDefault = isDefault;
        IsCustom = isCustom;
        ParagraphFormatting = paragraphFormatting
            ?? throw new ArgumentNullException(nameof(paragraphFormatting));
        CharacterFormatting = characterFormatting
            ?? throw new ArgumentNullException(nameof(characterFormatting));
        TableFormatting = tableFormatting
            ?? throw new ArgumentNullException(nameof(tableFormatting));
    }

    public string StyleId { get; }

    public string? Name { get; }

    public StyleKind Kind { get; }

    public string? BasedOnStyleId { get; }

    public string? LinkedStyleId { get; }

    public bool IsDefault { get; }

    public bool IsCustom { get; }

    public ParagraphFormatting ParagraphFormatting { get; }

    public CharacterFormatting CharacterFormatting { get; }

    public TableFormatting TableFormatting { get; }
}

/// <summary>
/// An immutable parsed Word document.
/// </summary>
public sealed record DocumentModel
{
    public DocumentModel(
        WordPackageKind packageKind,
        DocumentDefaults defaults,
        IEnumerable<StyleDefinition> styles,
        IEnumerable<SectionModel> sections)
    {
        PackageKind = packageKind;
        Defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        Styles = new ValueList<StyleDefinition>(
            styles ?? throw new ArgumentNullException(nameof(styles)));
        Sections = new ValueList<SectionModel>(
            sections ?? throw new ArgumentNullException(nameof(sections)));
    }

    public WordPackageKind PackageKind { get; }

    public DocumentDefaults Defaults { get; }

    public ValueList<StyleDefinition> Styles { get; }

    public ValueList<SectionModel> Sections { get; }
}

/// <summary>
/// A section and its content, with page settings resolved for that section.
/// </summary>
public sealed record SectionModel
{
    public SectionModel(
        StructuralLocation location,
        PageSettings pageSettings,
        IEnumerable<ParagraphModel> paragraphs,
        IEnumerable<TableModel> tables)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));

        if (Location.SectionIndex is null)
        {
            throw new ArgumentException(
                "A section model requires a section location.",
                nameof(location));
        }

        PageSettings = pageSettings
            ?? throw new ArgumentNullException(nameof(pageSettings));
        Paragraphs = new ValueList<ParagraphModel>(
            paragraphs ?? throw new ArgumentNullException(nameof(paragraphs)));
        Tables = new ValueList<TableModel>(
            tables ?? throw new ArgumentNullException(nameof(tables)));
    }

    public StructuralLocation Location { get; }

    public PageSettings PageSettings { get; }

    public ValueList<ParagraphModel> Paragraphs { get; }

    public ValueList<TableModel> Tables { get; }
}

/// <summary>
/// A paragraph with separate direct and effective formatting.
/// </summary>
public sealed record ParagraphModel
{
    public ParagraphModel(
        StructuralLocation location,
        int blockIndex,
        string? styleId,
        DocumentText text,
        ParagraphFormatting directFormatting,
        ParagraphFormatting effectiveFormatting,
        IEnumerable<RunModel> runs)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));

        if (Location.ParagraphIndex is null || Location.RunIndex is not null)
        {
            throw new ArgumentException(
                "A paragraph model requires a paragraph location without a run index.",
                nameof(location));
        }

        DomainGuard.NonNegative(blockIndex, nameof(blockIndex));
        BlockIndex = blockIndex;
        StyleId = DomainGuard.OptionalNonBlank(styleId, nameof(styleId));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        DirectFormatting = directFormatting
            ?? throw new ArgumentNullException(nameof(directFormatting));
        EffectiveFormatting = effectiveFormatting
            ?? throw new ArgumentNullException(nameof(effectiveFormatting));
        Runs = new ValueList<RunModel>(
            runs ?? throw new ArgumentNullException(nameof(runs)));
    }

    public StructuralLocation Location { get; }

    /// <summary>
    /// Gets the paragraph's order among paragraphs and tables in its container.
    /// </summary>
    public int BlockIndex { get; }

    public string? StyleId { get; }

    public DocumentText Text { get; }

    public ParagraphFormatting DirectFormatting { get; }

    public ParagraphFormatting EffectiveFormatting { get; }

    public ValueList<RunModel> Runs { get; }
}

/// <summary>
/// A text run with separate direct and effective formatting.
/// </summary>
public sealed record RunModel
{
    public RunModel(
        StructuralLocation location,
        string? styleId,
        DocumentText text,
        CharacterFormatting directFormatting,
        CharacterFormatting effectiveFormatting)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));

        if (Location.RunIndex is null)
        {
            throw new ArgumentException(
                "A run model requires a run location.",
                nameof(location));
        }

        StyleId = DomainGuard.OptionalNonBlank(styleId, nameof(styleId));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        DirectFormatting = directFormatting
            ?? throw new ArgumentNullException(nameof(directFormatting));
        EffectiveFormatting = effectiveFormatting
            ?? throw new ArgumentNullException(nameof(effectiveFormatting));
    }

    public StructuralLocation Location { get; }

    public string? StyleId { get; }

    public DocumentText Text { get; }

    public CharacterFormatting DirectFormatting { get; }

    public CharacterFormatting EffectiveFormatting { get; }
}

/// <summary>
/// A Word table and its rows.
/// </summary>
public sealed record TableModel
{
    public TableModel(
        StructuralLocation location,
        int blockIndex,
        string? styleId,
        TableFormatting directFormatting,
        TableFormatting effectiveFormatting,
        IEnumerable<RowModel> rows)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));

        if (Location.TableIndex is null
            || Location.RowIndex is not null
            || Location.CellIndex is not null)
        {
            throw new ArgumentException(
                "A table model requires a table location without row or cell indices.",
                nameof(location));
        }

        DomainGuard.NonNegative(blockIndex, nameof(blockIndex));
        BlockIndex = blockIndex;
        StyleId = DomainGuard.OptionalNonBlank(styleId, nameof(styleId));
        DirectFormatting = directFormatting
            ?? throw new ArgumentNullException(nameof(directFormatting));
        EffectiveFormatting = effectiveFormatting
            ?? throw new ArgumentNullException(nameof(effectiveFormatting));
        Rows = new ValueList<RowModel>(
            rows ?? throw new ArgumentNullException(nameof(rows)));
    }

    public StructuralLocation Location { get; }

    /// <summary>
    /// Gets the table's order among paragraphs and tables in its container.
    /// </summary>
    public int BlockIndex { get; }

    public string? StyleId { get; }

    public TableFormatting DirectFormatting { get; }

    public TableFormatting EffectiveFormatting { get; }

    public ValueList<RowModel> Rows { get; }
}

/// <summary>
/// A Word table row.
/// </summary>
public sealed record RowModel
{
    public RowModel(
        StructuralLocation location,
        RowFormatting directFormatting,
        RowFormatting effectiveFormatting,
        IEnumerable<CellModel> cells)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));

        if (Location.RowIndex is null || Location.CellIndex is not null)
        {
            throw new ArgumentException(
                "A row model requires a row location without a cell index.",
                nameof(location));
        }

        DirectFormatting = directFormatting
            ?? throw new ArgumentNullException(nameof(directFormatting));
        EffectiveFormatting = effectiveFormatting
            ?? throw new ArgumentNullException(nameof(effectiveFormatting));
        Cells = new ValueList<CellModel>(
            cells ?? throw new ArgumentNullException(nameof(cells)));
    }

    public StructuralLocation Location { get; }

    public RowFormatting DirectFormatting { get; }

    public RowFormatting EffectiveFormatting { get; }

    public ValueList<CellModel> Cells { get; }
}

/// <summary>
/// A Word table cell and its paragraphs.
/// </summary>
public sealed record CellModel
{
    public CellModel(
        StructuralLocation location,
        int gridSpan,
        CellFormatting directFormatting,
        CellFormatting effectiveFormatting,
        IEnumerable<ParagraphModel> paragraphs)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));

        if (Location.CellIndex is null || Location.ParagraphIndex is not null)
        {
            throw new ArgumentException(
                "A cell model requires a cell location without a paragraph index.",
                nameof(location));
        }

        if (gridSpan <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gridSpan),
                gridSpan,
                "A cell grid span must be greater than zero.");
        }

        GridSpan = gridSpan;
        DirectFormatting = directFormatting
            ?? throw new ArgumentNullException(nameof(directFormatting));
        EffectiveFormatting = effectiveFormatting
            ?? throw new ArgumentNullException(nameof(effectiveFormatting));
        Paragraphs = new ValueList<ParagraphModel>(
            paragraphs ?? throw new ArgumentNullException(nameof(paragraphs)));
    }

    public StructuralLocation Location { get; }

    public int GridSpan { get; }

    public CellFormatting DirectFormatting { get; }

    public CellFormatting EffectiveFormatting { get; }

    public ValueList<ParagraphModel> Paragraphs { get; }
}

internal static class DomainGuard
{
    public static string RequiredIdentifier(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "An identifier cannot be blank.",
                parameterName);
        }

        return value;
    }

    public static string? OptionalNonBlank(
        string? value,
        string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "An optional value must be null or non-blank.",
                parameterName);
        }

        return value;
    }

    public static void NonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A structural index cannot be negative.");
        }
    }
}
