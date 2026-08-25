using System.Text.Json.Serialization;

namespace PaperFormat.Domain;

/// <summary>
/// Page orientation after OOXML normalization.
/// </summary>
public enum PageOrientation
{
    Portrait,
    Landscape,
}

/// <summary>
/// Paragraph alignment after OOXML normalization.
/// </summary>
public enum ParagraphAlignment
{
    Left,
    Center,
    Right,
    Justified,
    Distributed,
}

/// <summary>
/// Table alignment after OOXML normalization.
/// </summary>
public enum TableAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Vertical cell alignment after OOXML normalization.
/// </summary>
public enum CellVerticalAlignment
{
    Top,
    Center,
    Bottom,
}

/// <summary>
/// The interpretation of a normalized line-spacing value.
/// </summary>
public enum LineSpacingKind
{
    Auto,
    Exact,
    AtLeast,
}

/// <summary>
/// A line-height multiple stored in OOXML's canonical 240ths-of-a-line unit.
/// </summary>
public readonly record struct LineMultiple
{
    public const int UnitsPerLine = 240;

    [JsonConstructor]
    public LineMultiple(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A line multiple must be greater than zero.");
        }

        Value = value;
    }

    /// <summary>
    /// Gets the multiple in 240ths of a line.
    /// </summary>
    public int Value { get; }

    /// <summary>
    /// Gets the human-readable factor, where one means single spacing.
    /// </summary>
    [JsonIgnore]
    public decimal Factor => Value / (decimal)UnitsPerLine;

    /// <summary>
    /// Normalizes a factor to 240ths of a line.
    /// </summary>
    public static LineMultiple FromFactor(decimal factor)
    {
        if (factor <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(factor),
                factor,
                "A line-spacing factor must be greater than zero.");
        }

        var units = decimal.Round(
            checked(factor * UnitsPerLine),
            0,
            MidpointRounding.AwayFromZero);

        return new LineMultiple(checked((int)units));
    }
}

/// <summary>
/// A normalized paragraph line-spacing value.
/// </summary>
public sealed record LineSpacing
{
    public LineSpacing(
        LineSpacingKind kind,
        Twip? length = null,
        LineMultiple? multiple = null)
    {
        var hasLength = length is not null;
        var hasMultiple = multiple is not null;

        if (kind == LineSpacingKind.Auto && (!hasMultiple || hasLength))
        {
            throw new ArgumentException(
                "Automatic line spacing requires a multiple and no length.",
                nameof(multiple));
        }

        if (kind != LineSpacingKind.Auto && (!hasLength || hasMultiple))
        {
            throw new ArgumentException(
                "Exact and at-least line spacing require a length and no multiple.",
                nameof(length));
        }

        if (length is { } lengthValue && lengthValue.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                length,
                "A line-spacing length must be greater than zero.");
        }

        Kind = kind;
        Length = length;
        Multiple = multiple;
    }

    public LineSpacingKind Kind { get; }

    public Twip? Length { get; }

    public LineMultiple? Multiple { get; }

    public static LineSpacing Automatic(LineMultiple multiple) =>
        new(LineSpacingKind.Auto, multiple: multiple);

    public static LineSpacing Exact(Twip length) =>
        new(LineSpacingKind.Exact, length);

    public static LineSpacing AtLeast(Twip length) =>
        new(LineSpacingKind.AtLeast, length);
}

/// <summary>
/// Script-specific font family names. A null value remains inherited or unknown.
/// </summary>
public sealed record FontFamilies(
    string? Ascii = null,
    string? HighAnsi = null,
    string? EastAsia = null,
    string? ComplexScript = null);

/// <summary>
/// Normalized indentation values. Negative twips are valid for hanging layouts.
/// </summary>
public sealed record Indentation(
    Twip? Left = null,
    Twip? Right = null,
    Twip? FirstLine = null,
    Twip? Hanging = null);

/// <summary>
/// Character formatting with null representing inherited or unknown values.
/// </summary>
public sealed record CharacterFormatting(
    FontFamilies? Fonts = null,
    Twip? FontSize = null,
    bool? Bold = null,
    bool? Italic = null,
    bool? AllCaps = null,
    bool? SmallCaps = null)
{
    public static CharacterFormatting Empty { get; } = new();

    [JsonIgnore]
    public bool IsEmpty =>
        Fonts is null
        && FontSize is null
        && Bold is null
        && Italic is null
        && AllCaps is null
        && SmallCaps is null;
}

/// <summary>
/// Paragraph formatting with null representing inherited or unknown values.
/// </summary>
public sealed record ParagraphFormatting(
    ParagraphAlignment? Alignment = null,
    LineSpacing? LineSpacing = null,
    Twip? SpaceBefore = null,
    Twip? SpaceAfter = null,
    Indentation? Indentation = null,
    bool? KeepNext = null,
    bool? KeepLines = null,
    bool? PageBreakBefore = null,
    bool? WidowControl = null)
{
    public static ParagraphFormatting Empty { get; } = new();

    [JsonIgnore]
    public bool IsEmpty =>
        Alignment is null
        && LineSpacing is null
        && SpaceBefore is null
        && SpaceAfter is null
        && Indentation is null
        && KeepNext is null
        && KeepLines is null
        && PageBreakBefore is null
        && WidowControl is null;
}

/// <summary>
/// Table formatting supported by the parser baseline.
/// </summary>
public sealed record TableFormatting(
    Twip? PreferredWidth = null,
    TableAlignment? Alignment = null,
    bool? AutoFit = null)
{
    public static TableFormatting Empty { get; } = new();

    [JsonIgnore]
    public bool IsEmpty =>
        PreferredWidth is null
        && Alignment is null
        && AutoFit is null;
}

/// <summary>
/// Row formatting supported by the parser baseline.
/// </summary>
public sealed record RowFormatting(
    Twip? Height = null,
    bool? RepeatAsHeader = null,
    bool? AllowBreakAcrossPages = null)
{
    public static RowFormatting Empty { get; } = new();

    [JsonIgnore]
    public bool IsEmpty =>
        Height is null
        && RepeatAsHeader is null
        && AllowBreakAcrossPages is null;
}

/// <summary>
/// Cell formatting supported by the parser baseline.
/// </summary>
public sealed record CellFormatting(
    Twip? PreferredWidth = null,
    CellVerticalAlignment? VerticalAlignment = null)
{
    public static CellFormatting Empty { get; } = new();

    [JsonIgnore]
    public bool IsEmpty =>
        PreferredWidth is null
        && VerticalAlignment is null;
}

/// <summary>
/// Page margins normalized to twips.
/// </summary>
public sealed record Margins(
    Twip? Top = null,
    Twip? Right = null,
    Twip? Bottom = null,
    Twip? Left = null,
    Twip? Header = null,
    Twip? Footer = null,
    Twip? Gutter = null);

/// <summary>
/// One explicit column definition in a non-equal-width section.
/// </summary>
public sealed record ColumnDefinition
{
    public ColumnDefinition(
        int index,
        Twip? width = null,
        Twip? spaceAfter = null)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                "A column index cannot be negative.");
        }

        Index = index;
        Width = width;
        SpaceAfter = spaceAfter;
    }

    public int Index { get; }

    public Twip? Width { get; }

    public Twip? SpaceAfter { get; }
}

/// <summary>
/// Section column settings normalized from OOXML.
/// </summary>
public sealed record Columns
{
    public Columns(
        int? count = null,
        Twip? spacing = null,
        bool? equalWidth = null,
        bool? separator = null,
        IEnumerable<ColumnDefinition>? definitions = null)
    {
        if (count is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "A specified column count must be greater than zero.");
        }

        Count = count;
        Spacing = spacing;
        EqualWidth = equalWidth;
        Separator = separator;
        Definitions = new ValueList<ColumnDefinition>(
            definitions ?? Array.Empty<ColumnDefinition>());
    }

    public int? Count { get; }

    public Twip? Spacing { get; }

    public bool? EqualWidth { get; }

    public bool? Separator { get; }

    public ValueList<ColumnDefinition> Definitions { get; }
}

/// <summary>
/// Section page settings normalized to canonical domain units.
/// </summary>
public sealed record PageSettings(
    Twip? Width,
    Twip? Height,
    PageOrientation? Orientation,
    Margins Margins,
    Columns Columns)
{
    public static PageSettings Empty { get; } = new(
        null,
        null,
        null,
        new Margins(),
        new Columns());
}
