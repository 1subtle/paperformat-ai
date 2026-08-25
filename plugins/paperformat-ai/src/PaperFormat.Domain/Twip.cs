using System.Globalization;
using System.Text.Json.Serialization;

namespace PaperFormat.Domain;

/// <summary>
/// A signed length in twentieths of a point, the canonical domain length unit.
/// </summary>
public readonly record struct Twip(long Value) : IComparable<Twip>
{
    public const int PerPoint = 20;
    public const int PerInch = 1440;

    /// <summary>
    /// Creates a twip length from points using midpoint-away-from-zero rounding.
    /// </summary>
    public static Twip FromPoints(decimal points) =>
        FromScaledValue(points, PerPoint);

    /// <summary>
    /// Creates a twip length from inches using midpoint-away-from-zero rounding.
    /// </summary>
    public static Twip FromInches(decimal inches) =>
        FromScaledValue(inches, PerInch);

    /// <summary>
    /// Creates a twip length from centimetres using the exact 2.54 cm/in ratio.
    /// </summary>
    public static Twip FromCentimeters(decimal centimeters) =>
        FromInches(centimeters / 2.54m);

    /// <summary>
    /// Creates a twip length from millimetres using the exact 25.4 mm/in ratio.
    /// </summary>
    public static Twip FromMillimeters(decimal millimeters) =>
        FromInches(millimeters / 25.4m);

    /// <summary>
    /// Gets the length in points.
    /// </summary>
    [JsonIgnore]
    public decimal Points => Value / (decimal)PerPoint;

    /// <summary>
    /// Gets the length in inches.
    /// </summary>
    [JsonIgnore]
    public decimal Inches => Value / (decimal)PerInch;

    /// <inheritdoc />
    public int CompareTo(Twip other) => Value.CompareTo(other.Value);

    public static bool operator <(Twip left, Twip right) =>
        left.Value < right.Value;

    public static bool operator <=(Twip left, Twip right) =>
        left.Value <= right.Value;

    public static bool operator >(Twip left, Twip right) =>
        left.Value > right.Value;

    public static bool operator >=(Twip left, Twip right) =>
        left.Value >= right.Value;

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Value} twip");

    private static Twip FromScaledValue(decimal value, int scale)
    {
        var scaled = checked(value * scale);
        var rounded = decimal.Round(scaled, 0, MidpointRounding.AwayFromZero);
        return new Twip(checked((long)rounded));
    }
}
