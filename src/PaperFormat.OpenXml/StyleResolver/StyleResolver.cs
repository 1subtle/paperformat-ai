using DocumentFormat.OpenXml;
using PaperFormat.Domain;

namespace PaperFormat.OpenXml;

internal sealed class StyleResolver
{
    private readonly Dictionary<string, RawStyle> _styles;
    private readonly Dictionary<string, StyleContribution> _resolved =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _cycleStyles = new(StringComparer.Ordinal);
    private readonly ICollection<ParseDiagnostic> _diagnostics;
    private readonly HashSet<string> _reportedDiagnosticCodes =
        new(StringComparer.Ordinal);
    private readonly string? _defaultParagraphStyleId;
    private readonly string? _defaultCharacterStyleId;
    private readonly string? _defaultTableStyleId;

    private StyleResolver(
        DocumentDefaults defaults,
        IReadOnlyList<RawStyle> styles,
        ICollection<ParseDiagnostic> diagnostics)
    {
        Defaults = defaults;
        _diagnostics = diagnostics;

        var byId = new Dictionary<string, RawStyle>(StringComparer.Ordinal);
        foreach (RawStyle style in styles)
        {
            if (!byId.TryAdd(style.StyleId, style))
            {
                AddDiagnostic(
                    WordDocumentParserDiagnosticCodes.DuplicateStyleId,
                    "A duplicate style identifier was ignored.");
            }
        }

        _styles = byId;
        _defaultParagraphStyleId = FindDefault(styles, StyleKind.Paragraph);
        _defaultCharacterStyleId = FindDefault(styles, StyleKind.Character);
        _defaultTableStyleId = FindDefault(styles, StyleKind.Table);

        DetectCycles(styles);
        DetectBrokenReferences(styles);

        Styles = styles
            .Where(style => ReferenceEquals(byId[style.StyleId], style))
            .Select(ToResolvedDefinition)
            .ToArray();
    }

    public DocumentDefaults Defaults { get; }

    public IReadOnlyList<StyleDefinition> Styles { get; }

    public static StyleResolver Create(
        OpenXmlElement? stylesRoot,
        ICollection<ParseDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (stylesRoot is null)
        {
            diagnostics.Add(new ParseDiagnostic(
                WordDocumentParserDiagnosticCodes.StylesPartMissing,
                ParseDiagnosticSeverity.Warning,
                "The package has no styles part; formatting remains inherited or unknown."));
            return new StyleResolver(
                DocumentDefaults.Empty,
                Array.Empty<RawStyle>(),
                diagnostics);
        }

        DocumentDefaults defaults = ReadDefaults(stylesRoot);
        List<RawStyle> styles = [];

        foreach (OpenXmlElement styleElement in
                 OpenXmlValueReader.Children(stylesRoot, "style"))
        {
            string? styleId =
                OpenXmlValueReader.Attribute(styleElement, "styleId");
            if (string.IsNullOrWhiteSpace(styleId))
            {
                diagnostics.Add(new ParseDiagnostic(
                    WordDocumentParserDiagnosticCodes.MissingStyleId,
                    ParseDiagnosticSeverity.Warning,
                    "A style without an identifier was ignored."));
                continue;
            }

            styles.Add(new RawStyle(
                styleId,
                OpenXmlValueReader.ChildValue(styleElement, "name"),
                ReadStyleKind(
                    OpenXmlValueReader.Attribute(styleElement, "type")),
                OpenXmlValueReader.ChildValue(styleElement, "basedOn"),
                OpenXmlValueReader.ChildValue(styleElement, "link"),
                OpenXmlValueReader.OnOffAttribute(styleElement, "default")
                    ?? false,
                OpenXmlValueReader.OnOffAttribute(styleElement, "customStyle")
                    ?? false,
                FormattingResolver.ReadParagraph(
                    OpenXmlValueReader.Child(styleElement, "pPr")),
                FormattingResolver.ReadCharacter(
                    OpenXmlValueReader.Child(styleElement, "rPr")),
                FormattingResolver.ReadTable(
                    OpenXmlValueReader.Child(styleElement, "tblPr"))));
        }

        return new StyleResolver(defaults, styles, diagnostics);
    }

    public ParagraphFormatting ResolveParagraph(
        string? paragraphStyleId,
        string? tableStyleId,
        ParagraphFormatting direct)
    {
        ParagraphFormatting effective = Defaults.Paragraph;
        effective = FormattingResolver.Overlay(
            effective,
            ContributionFor(tableStyleId ?? _defaultTableStyleId).Paragraph);
        effective = FormattingResolver.Overlay(
            effective,
            ContributionFor(paragraphStyleId ?? _defaultParagraphStyleId).Paragraph);
        return FormattingResolver.Overlay(effective, direct);
    }

    public CharacterFormatting ResolveCharacter(
        string? paragraphStyleId,
        string? tableStyleId,
        CharacterFormatting paragraphMarkDirect,
        string? runStyleId,
        CharacterFormatting runDirect)
    {
        CharacterFormatting effective = Defaults.Character;
        effective = FormattingResolver.Overlay(
            effective,
            ContributionFor(_defaultCharacterStyleId).Character);
        effective = FormattingResolver.Overlay(
            effective,
            ContributionFor(tableStyleId ?? _defaultTableStyleId).Character);

        string? effectiveParagraphStyleId =
            paragraphStyleId ?? _defaultParagraphStyleId;
        if (effectiveParagraphStyleId is not null
            && _styles.TryGetValue(
                effectiveParagraphStyleId,
                out RawStyle? paragraphStyle))
        {
            effective = FormattingResolver.Overlay(
                effective,
                ContributionFor(paragraphStyle.LinkedStyleId).Character);
        }

        effective = FormattingResolver.Overlay(
            effective,
            ContributionFor(effectiveParagraphStyleId).Character);
        effective = FormattingResolver.Overlay(effective, paragraphMarkDirect);
        effective = FormattingResolver.Overlay(
            effective,
            ContributionFor(runStyleId).Character);
        return FormattingResolver.Overlay(effective, runDirect);
    }

    public TableFormatting ResolveTable(
        string? tableStyleId,
        TableFormatting direct)
    {
        TableFormatting effective =
            ContributionFor(tableStyleId ?? _defaultTableStyleId).Table;
        return FormattingResolver.Overlay(effective, direct);
    }

    private static DocumentDefaults ReadDefaults(OpenXmlElement stylesRoot)
    {
        OpenXmlElement? defaults =
            OpenXmlValueReader.Child(stylesRoot, "docDefaults");
        OpenXmlElement? paragraphDefault =
            OpenXmlValueReader.Child(defaults, "pPrDefault");
        OpenXmlElement? runDefault =
            OpenXmlValueReader.Child(defaults, "rPrDefault");

        return new DocumentDefaults(
            FormattingResolver.ReadParagraph(
                OpenXmlValueReader.Child(paragraphDefault, "pPr")),
            FormattingResolver.ReadCharacter(
                OpenXmlValueReader.Child(runDefault, "rPr")));
    }

    private StyleDefinition ToResolvedDefinition(RawStyle style)
    {
        StyleContribution contribution = ContributionFor(style.StyleId);
        return new StyleDefinition(
            style.StyleId,
            style.Name,
            style.Kind,
            style.BasedOnStyleId,
            style.LinkedStyleId,
            style.IsDefault,
            style.IsCustom,
            FormattingResolver.Overlay(
                Defaults.Paragraph,
                contribution.Paragraph),
            FormattingResolver.Overlay(
                Defaults.Character,
                contribution.Character),
            contribution.Table);
    }

    private StyleContribution ContributionFor(string? styleId)
    {
        if (styleId is null)
        {
            return StyleContribution.Empty;
        }

        if (!_styles.TryGetValue(styleId, out RawStyle? style))
        {
            AddDiagnostic(
                WordDocumentParserDiagnosticCodes.UndefinedStyleReference,
                "A document element references an undefined style.");
            return StyleContribution.Empty;
        }

        if (_resolved.TryGetValue(styleId, out StyleContribution? cached))
        {
            return cached;
        }

        StyleContribution inherited = style.BasedOnStyleId is not null
            && !_cycleStyles.Contains(style.StyleId)
                ? ContributionFor(style.BasedOnStyleId)
                : StyleContribution.Empty;
        var resolved = new StyleContribution(
            FormattingResolver.Overlay(
                inherited.Paragraph,
                style.Paragraph),
            FormattingResolver.Overlay(
                inherited.Character,
                style.Character),
            FormattingResolver.Overlay(inherited.Table, style.Table));
        _resolved.Add(styleId, resolved);
        return resolved;
    }

    private void DetectBrokenReferences(IEnumerable<RawStyle> styles)
    {
        foreach (RawStyle style in styles)
        {
            if (style.BasedOnStyleId is not null
                && !_styles.ContainsKey(style.BasedOnStyleId))
            {
                AddDiagnostic(
                    WordDocumentParserDiagnosticCodes.MissingBaseStyle,
                    "A style references a missing base style; its own formatting was retained.");
            }

            if (style.LinkedStyleId is not null
                && !_styles.ContainsKey(style.LinkedStyleId))
            {
                AddDiagnostic(
                    WordDocumentParserDiagnosticCodes.MissingLinkedStyle,
                    "A style references a missing linked style; the link was ignored.");
            }
        }
    }

    private void DetectCycles(IEnumerable<RawStyle> styles)
    {
        foreach (RawStyle style in styles)
        {
            var path = new List<string>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            string? current = style.StyleId;

            while (current is not null && _styles.TryGetValue(current, out RawStyle? item))
            {
                if (positions.TryGetValue(current, out int cycleStart))
                {
                    foreach (string cycleStyle in path.Skip(cycleStart))
                    {
                        _cycleStyles.Add(cycleStyle);
                    }

                    AddDiagnostic(
                        WordDocumentParserDiagnosticCodes.StyleInheritanceCycle,
                        "A style inheritance cycle was detected; cyclic base links were ignored.");
                    break;
                }

                positions.Add(current, path.Count);
                path.Add(current);
                current = item.BasedOnStyleId;
            }
        }
    }

    private void AddDiagnostic(string code, string message)
    {
        if (_reportedDiagnosticCodes.Add(code))
        {
            _diagnostics.Add(new ParseDiagnostic(
                code,
                ParseDiagnosticSeverity.Warning,
                message));
        }
    }

    private static string? FindDefault(
        IEnumerable<RawStyle> styles,
        StyleKind kind) =>
        styles.FirstOrDefault(style => style.Kind == kind && style.IsDefault)
            ?.StyleId;

    private static StyleKind ReadStyleKind(string? value) =>
        value switch
        {
            "paragraph" => StyleKind.Paragraph,
            "character" => StyleKind.Character,
            "table" => StyleKind.Table,
            "numbering" => StyleKind.Numbering,
            _ => StyleKind.Unknown,
        };

    private sealed record RawStyle(
        string StyleId,
        string? Name,
        StyleKind Kind,
        string? BasedOnStyleId,
        string? LinkedStyleId,
        bool IsDefault,
        bool IsCustom,
        ParagraphFormatting Paragraph,
        CharacterFormatting Character,
        TableFormatting Table);

    private sealed record StyleContribution(
        ParagraphFormatting Paragraph,
        CharacterFormatting Character,
        TableFormatting Table)
    {
        public static StyleContribution Empty { get; } = new(
            ParagraphFormatting.Empty,
            CharacterFormatting.Empty,
            TableFormatting.Empty);
    }
}
