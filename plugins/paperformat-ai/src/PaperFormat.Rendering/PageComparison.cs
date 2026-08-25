namespace PaperFormat.Rendering;

public sealed record PageComparisonReport(
    string SchemaVersion,
    string Status,
    int BeforePageCount,
    int AfterPageCount,
    IReadOnlyList<PageComparisonFinding> Findings)
{
    public const string CurrentSchemaVersion = "1.0";
}

public sealed record PageComparisonFinding(
    string Code,
    string Severity,
    string Message,
    int? BeforePage,
    int? AfterPage);

/// <summary>
/// Deterministic rendered-page comparison shared by the CLI and local Web.
/// </summary>
public static class PageComparer
{
    public static PageComparisonReport Compare(
        string beforeDirectory,
        string afterDirectory)
    {
        PageInfo[] before = Pages(beforeDirectory);
        PageInfo[] after = Pages(afterDirectory);
        List<PageComparisonFinding> findings = [];
        if (before.Length != after.Length)
        {
            findings.Add(new PageComparisonFinding(
                "page_count_changed",
                "information",
                $"Pagination changed from {before.Length} to {after.Length} pages; "
                + "page count alone is not a format-only failure.",
                before.Length == 0 ? null : before.Length,
                after.Length == 0 ? null : after.Length));
        }

        int common = Math.Min(before.Length, after.Length);
        for (int index = 0; index < common; index++)
        {
            PageInfo source = before[index];
            PageInfo output = after[index];
            if (source.Width != output.Width
                || source.Height != output.Height)
            {
                findings.Add(new PageComparisonFinding(
                    "page_dimensions_changed",
                    "review",
                    $"Page {index + 1} changed rendered dimensions from "
                    + $"{source.Width}x{source.Height} to "
                    + $"{output.Width}x{output.Height}.",
                    index + 1,
                    index + 1));
            }

            if (!string.Equals(
                    source.Sha256,
                    output.Sha256,
                    StringComparison.Ordinal))
            {
                findings.Add(new PageComparisonFinding(
                    "page_pixels_changed",
                    "information",
                    $"Page {index + 1} has rendered pixel changes.",
                    index + 1,
                    index + 1));
            }

            if (output.LargestInteriorBlankRatio >= 0.45
                && source.LargestInteriorBlankRatio < 0.45)
            {
                findings.Add(new PageComparisonFinding(
                    "large_blank_region_introduced",
                    "blocking",
                    $"Page {index + 1} gained an unusually large internal blank region.",
                    index + 1,
                    index + 1));
            }
        }

        foreach (PageInfo output in after.Skip(common))
        {
            if (output.InkRatio < 0.00005)
            {
                findings.Add(new PageComparisonFinding(
                    "mostly_blank_page_introduced",
                    "blocking",
                    $"Output page {output.PageNumber} appears almost blank.",
                    null,
                    output.PageNumber));
            }
        }

        string status = findings.Any(
            item => item.Severity == "blocking")
            ? "failed"
            : findings.Any(item => item.Severity == "review")
                ? "needsConfirmation"
                : "passed";
        return new PageComparisonReport(
            PageComparisonReport.CurrentSchemaVersion,
            status,
            before.Length,
            after.Length,
            findings);
    }

    private static PageInfo[] Pages(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"Rendered-page directory does not exist: {fullPath}");
        }

        return Directory.EnumerateFiles(fullPath, "page-*.png")
            .Select(
                path =>
                {
                    (int width, int height, string sha256) =
                        PngInspector.Inspect(path);
                    PngPageAnalysis analysis =
                        PngInspector.Analyze(path);
                    return new PageInfo(
                        PageNumber(path),
                        width,
                        height,
                        sha256,
                        analysis.InkRatio,
                        analysis.LargestInteriorBlankRatio);
                })
            .OrderBy(item => item.PageNumber)
            .ToArray();
    }

    private static int PageNumber(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int separator = name.LastIndexOf('-');
        if (separator < 0
            || !int.TryParse(name[(separator + 1)..], out int value)
            || value <= 0)
        {
            throw new InvalidDataException(
                $"Unexpected rendered page name '{name}'.");
        }

        return value;
    }

    private sealed record PageInfo(
        int PageNumber,
        int Width,
        int Height,
        string Sha256,
        double InkRatio,
        double LargestInteriorBlankRatio);
}
