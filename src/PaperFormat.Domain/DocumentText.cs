using System.Globalization;
using System.Text.Json.Serialization;

namespace PaperFormat.Domain;

/// <summary>
/// Manuscript text kept in memory for classification and integrity checks.
/// Its string representation is always redacted to prevent accidental logging.
/// </summary>
public sealed record DocumentText
{
    public DocumentText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>
    /// Gets the manuscript text. Callers must not write this value to logs.
    /// </summary>
    [JsonIgnore]
    public string Value { get; }

    /// <summary>
    /// Gets the UTF-16 character count.
    /// </summary>
    [JsonPropertyName("length")]
    public int Length => Value.Length;

    /// <inheritdoc />
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "[document text redacted; length={0}]",
            Length);
}
