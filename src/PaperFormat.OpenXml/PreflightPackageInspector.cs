using System.IO.Compression;

namespace PaperFormat.OpenXml;

/// <summary>
/// Performs bounded, content-safe validation of DOCX and DOTX ZIP packages.
/// </summary>
public static class DocxPackagePreflight
{
    private const string ContentTypesEntryName = "[Content_Types].xml";
    private const string MainDocumentEntryName = "word/document.xml";

    private static readonly byte[][] ZipSignatures =
    [
        [0x50, 0x4B, 0x03, 0x04],
        [0x50, 0x4B, 0x05, 0x06],
        [0x50, 0x4B, 0x07, 0x08],
    ];

    /// <summary>
    /// Inspects a package from a filesystem path.
    /// </summary>
    public static PackagePreflightResult Inspect(
        string path,
        PackagePreflightOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A package path is required.", nameof(path));
        }

        PackagePreflightOptions effectiveOptions = options ?? new PackagePreflightOptions();
        ValidateOptions(effectiveOptions);

        PackagePreflightDiagnostic? extensionDiagnostic = ValidateExtension(path);
        if (extensionDiagnostic is not null)
        {
            return Failure(extensionDiagnostic);
        }

        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.SequentialScan);

            return InspectCore(stream, effectiveOptions);
        }
        catch (FileNotFoundException)
        {
            return Failure(new(
                PackagePreflightDiagnosticCode.FileNotFound,
                "The package file does not exist."));
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(new(
                PackagePreflightDiagnosticCode.FileNotFound,
                "The package file does not exist."));
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(new(
                PackagePreflightDiagnosticCode.FileReadFailed,
                "The package file could not be read."));
        }
        catch (IOException)
        {
            return Failure(new(
                PackagePreflightDiagnosticCode.FileReadFailed,
                "The package file could not be read."));
        }
    }

    /// <summary>
    /// Inspects a package stream using the supplied client filename for extension validation.
    /// Seekable streams are restored to their original position.
    /// </summary>
    public static PackagePreflightResult Inspect(
        Stream packageStream,
        string fileName,
        PackagePreflightOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("A client filename is required.", nameof(fileName));
        }

        PackagePreflightOptions effectiveOptions = options ?? new PackagePreflightOptions();
        ValidateOptions(effectiveOptions);

        PackagePreflightDiagnostic? extensionDiagnostic = ValidateExtension(fileName);
        if (extensionDiagnostic is not null)
        {
            return Failure(extensionDiagnostic);
        }

        if (!packageStream.CanRead)
        {
            return Failure(new(
                PackagePreflightDiagnosticCode.StreamNotReadable,
                "The package stream is not readable."));
        }

        return InspectCore(packageStream, effectiveOptions);
    }

    private static PackagePreflightResult InspectCore(
        Stream packageStream,
        PackagePreflightOptions options)
    {
        (MemoryStream? bufferedPackage, PackagePreflightDiagnostic? readDiagnostic) =
            BufferPackage(packageStream, options.MaxPackageBytes);

        if (readDiagnostic is not null)
        {
            return Failure(readDiagnostic);
        }

        if (bufferedPackage is null)
        {
            throw new InvalidOperationException(
                "Package buffering returned neither a package nor a diagnostic.");
        }

        using (bufferedPackage)
        {
            if (!HasZipSignature(bufferedPackage))
            {
                return Failure(new(
                    PackagePreflightDiagnosticCode.InvalidZipSignature,
                    "The file does not have a supported ZIP signature."));
            }

            return InspectArchive(bufferedPackage, options);
        }
    }

    private static PackagePreflightResult InspectArchive(
        MemoryStream package,
        PackagePreflightOptions options)
    {
        try
        {
            package.Position = 0;
            using ZipArchive archive = new(package, ZipArchiveMode.Read, leaveOpen: true);

            if (archive.Entries.Count > options.MaxEntryCount)
            {
                return new(
                    new(
                        package.Length,
                        archive.Entries.Count,
                        CompressedBytes: 0,
                        ExpandedBytes: 0),
                    [
                        new(
                            PackagePreflightDiagnosticCode.TooManyEntries,
                            "The ZIP package contains too many entries."),
                    ]);
            }

            List<PackagePreflightDiagnostic> diagnostics = [];
            HashSet<PackagePreflightDiagnosticCode> reportedCodes = [];
            bool hasContentTypes = false;
            bool hasMainDocument = false;
            long compressedBytes = 0;
            long expandedBytes = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string entryPath = entry.FullName;
                AddWithSaturation(ref compressedBytes, entry.CompressedLength);
                AddWithSaturation(ref expandedBytes, entry.Length);

                if (IsUnsafeEntryPath(entryPath))
                {
                    AddOnce(
                        diagnostics,
                        reportedCodes,
                        new(
                            PackagePreflightDiagnosticCode.UnsafeEntryPath,
                            "The ZIP package contains an unsafe entry path.",
                            SafeEntryPath(entryPath)));
                }

                if (entry.Length > options.MaxEntryExpandedBytes)
                {
                    AddOnce(
                        diagnostics,
                        reportedCodes,
                        new(
                            PackagePreflightDiagnosticCode.EntryTooLarge,
                            "A ZIP entry exceeds the expanded-size limit.",
                            SafeEntryPath(entryPath)));
                }

                if (expandedBytes > options.MaxTotalExpandedBytes)
                {
                    AddOnce(
                        diagnostics,
                        reportedCodes,
                        new(
                            PackagePreflightDiagnosticCode.ExpandedSizeTooLarge,
                            "The ZIP package exceeds the total expanded-size limit."));
                }

                if (ExceedsCompressionRatio(entry, options.MaxCompressionRatio))
                {
                    AddOnce(
                        diagnostics,
                        reportedCodes,
                        new(
                            PackagePreflightDiagnosticCode.CompressionRatioExceeded,
                            "A ZIP entry exceeds the allowed compression ratio.",
                            SafeEntryPath(entryPath)));
                }

                hasContentTypes |= string.Equals(
                    entryPath,
                    ContentTypesEntryName,
                    StringComparison.Ordinal);
                hasMainDocument |= string.Equals(
                    entryPath,
                    MainDocumentEntryName,
                    StringComparison.Ordinal);
            }

            if (!hasContentTypes)
            {
                diagnostics.Add(new(
                    PackagePreflightDiagnosticCode.MissingContentTypes,
                    "The OOXML content-types part is missing."));
            }

            if (!hasMainDocument)
            {
                diagnostics.Add(new(
                    PackagePreflightDiagnosticCode.MissingMainDocument,
                    "The Word main document part is missing."));
            }

            PackagePreflightSummary summary = new(
                package.Length,
                archive.Entries.Count,
                compressedBytes,
                expandedBytes);

            if (diagnostics.Count > 0)
            {
                return new(summary, diagnostics.ToArray());
            }

            PackagePreflightDiagnostic? decompressionDiagnostic =
                VerifyEntriesCanBeDecompressed(archive, options);

            return decompressionDiagnostic is null
                ? new(summary, Array.Empty<PackagePreflightDiagnostic>())
                : new(summary, [decompressionDiagnostic]);
        }
        catch (InvalidDataException)
        {
            return Failure(new(
                PackagePreflightDiagnosticCode.CorruptZip,
                "The ZIP package is corrupt or unsupported."));
        }
        catch (IOException)
        {
            return Failure(new(
                PackagePreflightDiagnosticCode.CorruptZip,
                "The ZIP package is corrupt or unsupported."));
        }
        catch (NotSupportedException)
        {
            return Failure(new(
                PackagePreflightDiagnosticCode.CorruptZip,
                "The ZIP package is corrupt or unsupported."));
        }
    }

    private static PackagePreflightDiagnostic? VerifyEntriesCanBeDecompressed(
        ZipArchive archive,
        PackagePreflightOptions options)
    {
        byte[] buffer = new byte[81920];
        long totalExpandedBytes = 0;

        try
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.Length == 0 && IsDirectoryEntry(entry.FullName))
                {
                    continue;
                }

                long entryExpandedBytes = 0;
                using Stream entryStream = entry.Open();
                int bytesRead;

                while ((bytesRead = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    AddWithSaturation(ref entryExpandedBytes, bytesRead);
                    AddWithSaturation(ref totalExpandedBytes, bytesRead);

                    if (entryExpandedBytes > options.MaxEntryExpandedBytes
                        || totalExpandedBytes > options.MaxTotalExpandedBytes)
                    {
                        return new(
                            PackagePreflightDiagnosticCode.CorruptZip,
                            "The ZIP package expanded beyond its declared safe bounds.",
                            SafeEntryPath(entry.FullName));
                    }
                }

                if (entryExpandedBytes != entry.Length)
                {
                    return new(
                        PackagePreflightDiagnosticCode.CorruptZip,
                        "A ZIP entry does not match its declared expanded size.",
                        SafeEntryPath(entry.FullName));
                }
            }
        }
        catch (InvalidDataException)
        {
            return new(
                PackagePreflightDiagnosticCode.CorruptZip,
                "The ZIP package is corrupt or unsupported.");
        }
        catch (IOException)
        {
            return new(
                PackagePreflightDiagnosticCode.CorruptZip,
                "The ZIP package is corrupt or unsupported.");
        }
        catch (NotSupportedException)
        {
            return new(
                PackagePreflightDiagnosticCode.CorruptZip,
                "The ZIP package is corrupt or unsupported.");
        }

        return null;
    }

    private static (
        MemoryStream? Package,
        PackagePreflightDiagnostic? Diagnostic) BufferPackage(
        Stream source,
        long maxPackageBytes)
    {
        long? originalPosition = source.CanSeek ? source.Position : null;
        MemoryStream destination = new();
        byte[] buffer = new byte[81920];

        try
        {
            int bytesRead;
            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (destination.Length > maxPackageBytes - bytesRead)
                {
                    destination.Dispose();
                    return (
                        null,
                        new(
                            PackagePreflightDiagnosticCode.PackageTooLarge,
                            "The ZIP package exceeds the compressed-size limit."));
                }

                destination.Write(buffer, 0, bytesRead);
            }

            destination.Position = 0;
            return (destination, null);
        }
        catch (IOException)
        {
            destination.Dispose();
            return (
                null,
                new(
                    PackagePreflightDiagnosticCode.FileReadFailed,
                    "The package stream could not be read."));
        }
        finally
        {
            if (originalPosition.HasValue)
            {
                source.Position = originalPosition.Value;
            }
        }
    }

    private static bool HasZipSignature(Stream package)
    {
        Span<byte> signature = stackalloc byte[4];
        package.Position = 0;
        int bytesRead = package.Read(signature);
        package.Position = 0;

        if (bytesRead != signature.Length)
        {
            return false;
        }

        foreach (byte[] candidate in ZipSignatures)
        {
            if (signature.SequenceEqual(candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static PackagePreflightDiagnostic? ValidateExtension(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".dotx", StringComparison.OrdinalIgnoreCase)
                ? null
                : new(
                    PackagePreflightDiagnosticCode.UnsupportedExtension,
                    "Only DOCX and DOTX package extensions are supported.");
    }

    private static bool IsUnsafeEntryPath(string entryPath)
    {
        if (string.IsNullOrEmpty(entryPath)
            || entryPath[0] is '/' or '\\'
            || IsWindowsAbsolutePath(entryPath))
        {
            return true;
        }

        int segmentStart = 0;
        for (int index = 0; index <= entryPath.Length; index++)
        {
            bool isBoundary = index == entryPath.Length
                || entryPath[index] is '/' or '\\';
            if (!isBoundary)
            {
                continue;
            }

            if (index - segmentStart == 2
                && entryPath[segmentStart] == '.'
                && entryPath[segmentStart + 1] == '.')
            {
                return true;
            }

            segmentStart = index + 1;
        }

        return false;
    }

    private static bool IsWindowsAbsolutePath(string entryPath) =>
        entryPath.Length >= 3
        && char.IsAsciiLetter(entryPath[0])
        && entryPath[1] == ':'
        && entryPath[2] is '/' or '\\';

    private static bool IsDirectoryEntry(string entryPath) =>
        entryPath.EndsWith('/')
        || entryPath.EndsWith('\\');

    private static bool ExceedsCompressionRatio(
        ZipArchiveEntry entry,
        double maxCompressionRatio)
    {
        if (entry.Length == 0)
        {
            return false;
        }

        return entry.CompressedLength == 0
            || (double)entry.Length / entry.CompressedLength > maxCompressionRatio;
    }

    private static string SafeEntryPath(string entryPath)
    {
        const int maxLength = 200;
        int sanitizedLength = Math.Min(entryPath.Length, maxLength);
        char[] sanitized = new char[sanitizedLength];
        for (int index = 0; index < sanitized.Length; index++)
        {
            char character = entryPath[index];
            sanitized[index] = char.IsControl(character) ? '\uFFFD' : character;
        }

        return new string(sanitized);
    }

    private static void AddOnce(
        List<PackagePreflightDiagnostic> diagnostics,
        HashSet<PackagePreflightDiagnosticCode> reportedCodes,
        PackagePreflightDiagnostic diagnostic)
    {
        if (reportedCodes.Add(diagnostic.Code))
        {
            diagnostics.Add(diagnostic);
        }
    }

    private static void AddWithSaturation(ref long total, long value)
    {
        total = value > long.MaxValue - total
            ? long.MaxValue
            : total + value;
    }

    private static PackagePreflightResult Failure(PackagePreflightDiagnostic diagnostic) =>
        new(null, [diagnostic]);

    private static void ValidateOptions(PackagePreflightOptions options)
    {
        if (options.MaxPackageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum package bytes must be positive.");
        }

        if (options.MaxEntryCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum entry count must be positive.");
        }

        if (options.MaxEntryExpandedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum entry expanded bytes must be positive.");
        }

        if (options.MaxTotalExpandedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum total expanded bytes must be positive.");
        }

        if (!double.IsFinite(options.MaxCompressionRatio)
            || options.MaxCompressionRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum compression ratio must be finite and positive.");
        }
    }
}
