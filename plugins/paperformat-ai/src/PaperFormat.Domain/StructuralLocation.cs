using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace PaperFormat.Domain;

/// <summary>
/// A package part that can contain document structures.
/// </summary>
public enum DocumentPartKind
{
    MainDocument,
    Header,
    Footer,
    Footnote,
    Endnote,
    Comment,
}

/// <summary>
/// A deterministic OOXML structural path. It intentionally contains no
/// rendered page number or manuscript text.
/// </summary>
public sealed record StructuralLocation : IComparable<StructuralLocation>
{
    public StructuralLocation(
        DocumentPartKind part,
        int? partIndex = null,
        int? sectionIndex = null,
        int? paragraphIndex = null,
        int? tableIndex = null,
        int? rowIndex = null,
        int? cellIndex = null,
        int? runIndex = null,
        string? relationshipId = null)
    {
        ValidateNonNegative(partIndex, nameof(partIndex));
        ValidateNonNegative(sectionIndex, nameof(sectionIndex));
        ValidateNonNegative(paragraphIndex, nameof(paragraphIndex));
        ValidateNonNegative(tableIndex, nameof(tableIndex));
        ValidateNonNegative(rowIndex, nameof(rowIndex));
        ValidateNonNegative(cellIndex, nameof(cellIndex));
        ValidateNonNegative(runIndex, nameof(runIndex));

        if (rowIndex is not null && tableIndex is null)
        {
            throw new ArgumentException(
                "A row location requires a table index.",
                nameof(rowIndex));
        }

        if (cellIndex is not null && rowIndex is null)
        {
            throw new ArgumentException(
                "A cell location requires a row index.",
                nameof(cellIndex));
        }

        if (runIndex is not null && paragraphIndex is null)
        {
            throw new ArgumentException(
                "A run location requires a paragraph index.",
                nameof(runIndex));
        }

        if (part == DocumentPartKind.MainDocument && partIndex is not null)
        {
            throw new ArgumentException(
                "The main document part does not have a part index.",
                nameof(partIndex));
        }

        if (relationshipId is not null && string.IsNullOrWhiteSpace(relationshipId))
        {
            throw new ArgumentException(
                "A relationship identifier cannot be blank.",
                nameof(relationshipId));
        }

        Part = part;
        PartIndex = partIndex;
        SectionIndex = sectionIndex;
        ParagraphIndex = paragraphIndex;
        TableIndex = tableIndex;
        RowIndex = rowIndex;
        CellIndex = cellIndex;
        RunIndex = runIndex;
        RelationshipId = relationshipId;
    }

    public DocumentPartKind Part { get; }

    public int? PartIndex { get; }

    public int? SectionIndex { get; }

    public int? ParagraphIndex { get; }

    public int? TableIndex { get; }

    public int? RowIndex { get; }

    public int? CellIndex { get; }

    public int? RunIndex { get; }

    public string? RelationshipId { get; }

    /// <summary>
    /// Gets a stable, text-safe path suitable for issue identifiers and reports.
    /// </summary>
    [JsonIgnore]
    public string CanonicalPath
    {
        get
        {
            var builder = new StringBuilder(GetPartName(Part));

            AppendIndex(builder, PartIndex);
            AppendSegment(builder, "section", SectionIndex);
            AppendSegment(builder, "table", TableIndex);
            AppendSegment(builder, "row", RowIndex);
            AppendSegment(builder, "cell", CellIndex);
            AppendSegment(builder, "paragraph", ParagraphIndex);
            AppendSegment(builder, "run", RunIndex);

            if (RelationshipId is not null)
            {
                builder.Append("/relationship[");
                builder.Append(Uri.EscapeDataString(RelationshipId));
                builder.Append(']');
            }

            return builder.ToString();
        }
    }

    /// <inheritdoc />
    public int CompareTo(StructuralLocation? other)
    {
        if (other is null)
        {
            return 1;
        }

        var comparison = Part.CompareTo(other.Part);
        comparison = comparison != 0
            ? comparison
            : CompareNullable(PartIndex, other.PartIndex);
        comparison = comparison != 0
            ? comparison
            : CompareNullable(SectionIndex, other.SectionIndex);
        comparison = comparison != 0
            ? comparison
            : CompareNullable(TableIndex, other.TableIndex);
        comparison = comparison != 0
            ? comparison
            : CompareNullable(RowIndex, other.RowIndex);
        comparison = comparison != 0
            ? comparison
            : CompareNullable(CellIndex, other.CellIndex);
        comparison = comparison != 0
            ? comparison
            : CompareNullable(ParagraphIndex, other.ParagraphIndex);
        comparison = comparison != 0
            ? comparison
            : CompareNullable(RunIndex, other.RunIndex);

        return comparison != 0
            ? comparison
            : string.Compare(
                RelationshipId,
                other.RelationshipId,
                StringComparison.Ordinal);
    }

    public static bool operator <(
        StructuralLocation? left,
        StructuralLocation? right) =>
        Comparer<StructuralLocation>.Default.Compare(left, right) < 0;

    public static bool operator <=(
        StructuralLocation? left,
        StructuralLocation? right) =>
        Comparer<StructuralLocation>.Default.Compare(left, right) <= 0;

    public static bool operator >(
        StructuralLocation? left,
        StructuralLocation? right) =>
        Comparer<StructuralLocation>.Default.Compare(left, right) > 0;

    public static bool operator >=(
        StructuralLocation? left,
        StructuralLocation? right) =>
        Comparer<StructuralLocation>.Default.Compare(left, right) >= 0;

    /// <inheritdoc />
    public override string ToString() => CanonicalPath;

    private static void ValidateNonNegative(int? value, string parameterName)
    {
        if (value is < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A structural index cannot be negative.");
        }
    }

    private static string GetPartName(DocumentPartKind part) =>
        part switch
        {
            DocumentPartKind.MainDocument => "main",
            DocumentPartKind.Header => "header",
            DocumentPartKind.Footer => "footer",
            DocumentPartKind.Footnote => "footnote",
            DocumentPartKind.Endnote => "endnote",
            DocumentPartKind.Comment => "comment",
            _ => throw new ArgumentOutOfRangeException(
                nameof(part),
                part,
                "Unknown document part kind."),
        };

    private static void AppendIndex(StringBuilder builder, int? index)
    {
        if (index is not null)
        {
            builder.Append('[');
            builder.Append(index.Value.ToString(CultureInfo.InvariantCulture));
            builder.Append(']');
        }
    }

    private static void AppendSegment(
        StringBuilder builder,
        string name,
        int? index)
    {
        if (index is not null)
        {
            builder.Append('/');
            builder.Append(name);
            AppendIndex(builder, index);
        }
    }

    private static int CompareNullable(int? left, int? right)
    {
        if (left is null)
        {
            return right is null ? 0 : -1;
        }

        return right is null ? 1 : left.Value.CompareTo(right.Value);
    }
}
