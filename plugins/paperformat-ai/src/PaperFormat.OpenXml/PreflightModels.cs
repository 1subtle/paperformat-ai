namespace PaperFormat.OpenXml;

/// <summary>
/// Limits applied before an OOXML package is handed to the document parser.
/// </summary>
public sealed class PackagePreflightOptions
{
    public const long DefaultMaxPackageBytes = 30L * 1024 * 1024;
    public const int DefaultMaxEntryCount = 10_000;
    public const long DefaultMaxEntryExpandedBytes = 128L * 1024 * 1024;
    public const long DefaultMaxTotalExpandedBytes = 512L * 1024 * 1024;
    public const double DefaultMaxCompressionRatio = 100;

    public long MaxPackageBytes { get; init; } = DefaultMaxPackageBytes;

    public int MaxEntryCount { get; init; } = DefaultMaxEntryCount;

    public long MaxEntryExpandedBytes { get; init; } = DefaultMaxEntryExpandedBytes;

    public long MaxTotalExpandedBytes { get; init; } = DefaultMaxTotalExpandedBytes;

    public double MaxCompressionRatio { get; init; } = DefaultMaxCompressionRatio;
}

/// <summary>
/// Stable codes for package preflight failures.
/// </summary>
public enum PackagePreflightDiagnosticCode
{
    UnsupportedExtension,
    FileNotFound,
    FileReadFailed,
    StreamNotReadable,
    PackageTooLarge,
    InvalidZipSignature,
    CorruptZip,
    TooManyEntries,
    EntryTooLarge,
    ExpandedSizeTooLarge,
    CompressionRatioExceeded,
    UnsafeEntryPath,
    MissingContentTypes,
    MissingMainDocument,
}

/// <summary>
/// A content-safe package preflight failure.
/// </summary>
/// <param name="Code">Machine-readable failure code.</param>
/// <param name="Message">Static diagnostic text that never contains document body text.</param>
/// <param name="EntryPath">The first relevant ZIP entry path, when applicable.</param>
public sealed record PackagePreflightDiagnostic(
    PackagePreflightDiagnosticCode Code,
    string Message,
    string? EntryPath = null);

/// <summary>
/// Non-content package metadata collected during preflight.
/// </summary>
public sealed record PackagePreflightSummary(
    long PackageBytes,
    int EntryCount,
    long CompressedBytes,
    long ExpandedBytes);

/// <summary>
/// Result of a DOCX or DOTX package preflight.
/// </summary>
public sealed record PackagePreflightResult(
    PackagePreflightSummary? Summary,
    IReadOnlyList<PackagePreflightDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;
}
