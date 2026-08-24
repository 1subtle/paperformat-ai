namespace PaperFormat.Domain;

/// <summary>
/// Content-safe inventory of structural resources in one Word package.
/// </summary>
public sealed record DocumentResourceInventory
{
    public DocumentResourceInventory(
        int paragraphCount,
        int runCount,
        int tableCount,
        int imageCount,
        int equationCount,
        int hyperlinkCount,
        int bookmarkCount,
        int fieldCount,
        int footnoteCount,
        int endnoteCount)
    {
        DomainGuard.NonNegative(paragraphCount, nameof(paragraphCount));
        DomainGuard.NonNegative(runCount, nameof(runCount));
        DomainGuard.NonNegative(tableCount, nameof(tableCount));
        DomainGuard.NonNegative(imageCount, nameof(imageCount));
        DomainGuard.NonNegative(equationCount, nameof(equationCount));
        DomainGuard.NonNegative(hyperlinkCount, nameof(hyperlinkCount));
        DomainGuard.NonNegative(bookmarkCount, nameof(bookmarkCount));
        DomainGuard.NonNegative(fieldCount, nameof(fieldCount));
        DomainGuard.NonNegative(footnoteCount, nameof(footnoteCount));
        DomainGuard.NonNegative(endnoteCount, nameof(endnoteCount));
        ParagraphCount = paragraphCount;
        RunCount = runCount;
        TableCount = tableCount;
        ImageCount = imageCount;
        EquationCount = equationCount;
        HyperlinkCount = hyperlinkCount;
        BookmarkCount = bookmarkCount;
        FieldCount = fieldCount;
        FootnoteCount = footnoteCount;
        EndnoteCount = endnoteCount;
    }

    public int ParagraphCount { get; }
    public int RunCount { get; }
    public int TableCount { get; }
    public int ImageCount { get; }
    public int EquationCount { get; }
    public int HyperlinkCount { get; }
    public int BookmarkCount { get; }
    public int FieldCount { get; }
    public int FootnoteCount { get; }
    public int EndnoteCount { get; }
}

/// <summary>
/// Content-safe section layout summary emitted by the Agent CLI.
/// </summary>
public sealed record SectionInspection
{
    public SectionInspection(
        int sectionIndex,
        PageSettings pageSettings,
        int paragraphCount,
        int tableCount)
    {
        DomainGuard.NonNegative(sectionIndex, nameof(sectionIndex));
        ArgumentNullException.ThrowIfNull(pageSettings);
        DomainGuard.NonNegative(paragraphCount, nameof(paragraphCount));
        DomainGuard.NonNegative(tableCount, nameof(tableCount));
        SectionIndex = sectionIndex;
        PageSettings = pageSettings;
        ParagraphCount = paragraphCount;
        TableCount = tableCount;
    }

    public int SectionIndex { get; }
    public PageSettings PageSettings { get; }
    public int ParagraphCount { get; }
    public int TableCount { get; }
}

/// <summary>
/// Versioned, content-safe document inspection contract.
/// </summary>
public sealed record DocumentInspection
{
    public const string CurrentSchemaVersion = "1.0";

    public DocumentInspection(
        string sourceSha256,
        WordPackageKind packageKind,
        int styleCount,
        DocumentResourceInventory resources,
        IEnumerable<SectionInspection> sections,
        IEnumerable<ParseDiagnostic> diagnostics,
        string schemaVersion = CurrentSchemaVersion)
    {
        SchemaVersion = DomainGuard.RequiredIdentifier(
            schemaVersion,
            nameof(schemaVersion));
        SourceSha256 = DomainGuard.RequiredIdentifier(
            sourceSha256,
            nameof(sourceSha256));
        PackageKind = packageKind;
        DomainGuard.NonNegative(styleCount, nameof(styleCount));
        StyleCount = styleCount;
        Resources = resources
            ?? throw new ArgumentNullException(nameof(resources));
        Sections = new ValueList<SectionInspection>(
            sections ?? throw new ArgumentNullException(nameof(sections)));
        Diagnostics = new ValueList<ParseDiagnostic>(
            diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)));
    }

    public string SchemaVersion { get; }

    public string SourceSha256 { get; }

    public WordPackageKind PackageKind { get; }

    public int StyleCount { get; }

    public DocumentResourceInventory Resources { get; }

    public ValueList<SectionInspection> Sections { get; }

    public ValueList<ParseDiagnostic> Diagnostics { get; }
}
