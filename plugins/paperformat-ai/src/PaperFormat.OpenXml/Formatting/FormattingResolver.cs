using System.Globalization;
using DocumentFormat.OpenXml;
using PaperFormat.Domain;

namespace PaperFormat.OpenXml;

internal static class FormattingResolver
{
    public static ParagraphFormatting ReadParagraph(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return ParagraphFormatting.Empty;
        }

        OpenXmlElement? spacing = OpenXmlValueReader.Child(properties, "spacing");
        OpenXmlElement? indentation = OpenXmlValueReader.Child(properties, "ind");

        return new ParagraphFormatting(
            Alignment: ReadParagraphAlignment(
                OpenXmlValueReader.ChildValue(properties, "jc")),
            LineSpacing: ReadLineSpacing(spacing),
            SpaceBefore: OpenXmlValueReader.TwipAttribute(spacing, "before"),
            SpaceAfter: OpenXmlValueReader.TwipAttribute(spacing, "after"),
            Indentation: ReadIndentation(indentation),
            KeepNext: OpenXmlValueReader.OnOffElement(
                OpenXmlValueReader.Child(properties, "keepNext")),
            KeepLines: OpenXmlValueReader.OnOffElement(
                OpenXmlValueReader.Child(properties, "keepLines")),
            PageBreakBefore: OpenXmlValueReader.OnOffElement(
                OpenXmlValueReader.Child(properties, "pageBreakBefore")),
            WidowControl: OpenXmlValueReader.OnOffElement(
                OpenXmlValueReader.Child(properties, "widowControl")));
    }

    public static CharacterFormatting ReadCharacter(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return CharacterFormatting.Empty;
        }

        OpenXmlElement? fonts = OpenXmlValueReader.Child(properties, "rFonts");

        return new CharacterFormatting(
            Fonts: ReadFonts(fonts),
            FontSize: ReadHalfPointSize(
                OpenXmlValueReader.Child(properties, "sz"))
                ?? ReadHalfPointSize(
                    OpenXmlValueReader.Child(properties, "szCs")),
            Bold: OpenXmlValueReader.OnOffElement(
                OpenXmlValueReader.Child(properties, "b"))
                ?? OpenXmlValueReader.OnOffElement(
                    OpenXmlValueReader.Child(properties, "bCs")),
            Italic: OpenXmlValueReader.OnOffElement(
                OpenXmlValueReader.Child(properties, "i"))
                ?? OpenXmlValueReader.OnOffElement(
                    OpenXmlValueReader.Child(properties, "iCs")),
            AllCaps: OpenXmlValueReader.OnOffElement(
                OpenXmlValueReader.Child(properties, "caps")),
            SmallCaps: OpenXmlValueReader.OnOffElement(
                OpenXmlValueReader.Child(properties, "smallCaps")));
    }

    public static TableFormatting ReadTable(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return TableFormatting.Empty;
        }

        OpenXmlElement? width = OpenXmlValueReader.Child(properties, "tblW");
        OpenXmlElement? layout = OpenXmlValueReader.Child(properties, "tblLayout");

        return new TableFormatting(
            PreferredWidth: ReadDxaWidth(width),
            Alignment: ReadTableAlignment(
                OpenXmlValueReader.ChildValue(properties, "jc")),
            AutoFit: OpenXmlValueReader.Attribute(layout, "type") switch
            {
                "autofit" => true,
                "fixed" => false,
                _ => null,
            });
    }

    public static RowFormatting ReadRow(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return RowFormatting.Empty;
        }

        OpenXmlElement? height = OpenXmlValueReader.Child(properties, "trHeight");
        bool? cannotSplit = OpenXmlValueReader.OnOffElement(
            OpenXmlValueReader.Child(properties, "cantSplit"));

        return new RowFormatting(
            Height: OpenXmlValueReader.TwipAttribute(height, "val"),
            RepeatAsHeader: OpenXmlValueReader.OnOffElement(
                OpenXmlValueReader.Child(properties, "tblHeader")),
            AllowBreakAcrossPages: cannotSplit is null
                ? null
                : !cannotSplit.Value);
    }

    public static CellFormatting ReadCell(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return CellFormatting.Empty;
        }

        return new CellFormatting(
            PreferredWidth: ReadDxaWidth(
                OpenXmlValueReader.Child(properties, "tcW")),
            VerticalAlignment: ReadCellVerticalAlignment(
                OpenXmlValueReader.ChildValue(properties, "vAlign")));
    }

    public static PageSettings ReadPageSettings(OpenXmlElement? sectionProperties)
    {
        if (sectionProperties is null)
        {
            return PageSettings.Empty;
        }

        OpenXmlElement? size = OpenXmlValueReader.Child(sectionProperties, "pgSz");
        OpenXmlElement? margins = OpenXmlValueReader.Child(sectionProperties, "pgMar");
        OpenXmlElement? columns = OpenXmlValueReader.Child(sectionProperties, "cols");

        return new PageSettings(
            Width: OpenXmlValueReader.TwipAttribute(size, "w"),
            Height: OpenXmlValueReader.TwipAttribute(size, "h"),
            Orientation: ReadPageOrientation(
                OpenXmlValueReader.Attribute(size, "orient"))
                ?? PageOrientation.Portrait,
            Margins: new Margins(
                Top: OpenXmlValueReader.TwipAttribute(margins, "top"),
                Right: OpenXmlValueReader.TwipAttribute(margins, "right"),
                Bottom: OpenXmlValueReader.TwipAttribute(margins, "bottom"),
                Left: OpenXmlValueReader.TwipAttribute(margins, "left"),
                Header: OpenXmlValueReader.TwipAttribute(margins, "header"),
                Footer: OpenXmlValueReader.TwipAttribute(margins, "footer"),
                Gutter: OpenXmlValueReader.TwipAttribute(margins, "gutter")),
            Columns: ReadColumns(columns));
    }

    public static ParagraphFormatting Overlay(
        ParagraphFormatting inherited,
        ParagraphFormatting direct) =>
        new(
            Alignment: direct.Alignment ?? inherited.Alignment,
            LineSpacing: direct.LineSpacing ?? inherited.LineSpacing,
            SpaceBefore: direct.SpaceBefore ?? inherited.SpaceBefore,
            SpaceAfter: direct.SpaceAfter ?? inherited.SpaceAfter,
            Indentation: Overlay(inherited.Indentation, direct.Indentation),
            KeepNext: direct.KeepNext ?? inherited.KeepNext,
            KeepLines: direct.KeepLines ?? inherited.KeepLines,
            PageBreakBefore:
                direct.PageBreakBefore ?? inherited.PageBreakBefore,
            WidowControl: direct.WidowControl ?? inherited.WidowControl);

    public static CharacterFormatting Overlay(
        CharacterFormatting inherited,
        CharacterFormatting direct) =>
        new(
            Fonts: Overlay(inherited.Fonts, direct.Fonts),
            FontSize: direct.FontSize ?? inherited.FontSize,
            Bold: direct.Bold ?? inherited.Bold,
            Italic: direct.Italic ?? inherited.Italic,
            AllCaps: direct.AllCaps ?? inherited.AllCaps,
            SmallCaps: direct.SmallCaps ?? inherited.SmallCaps);

    public static TableFormatting Overlay(
        TableFormatting inherited,
        TableFormatting direct) =>
        new(
            PreferredWidth: direct.PreferredWidth ?? inherited.PreferredWidth,
            Alignment: direct.Alignment ?? inherited.Alignment,
            AutoFit: direct.AutoFit ?? inherited.AutoFit);

    private static FontFamilies? ReadFonts(OpenXmlElement? fonts)
    {
        if (fonts is null)
        {
            return null;
        }

        string? ascii = OpenXmlValueReader.Attribute(fonts, "ascii");
        string? highAnsi = OpenXmlValueReader.Attribute(fonts, "hAnsi");
        string? eastAsia = OpenXmlValueReader.Attribute(fonts, "eastAsia");
        string? complexScript = OpenXmlValueReader.Attribute(fonts, "cs");

        return ascii is null
            && highAnsi is null
            && eastAsia is null
            && complexScript is null
                ? null
                : new FontFamilies(
                    ascii,
                    highAnsi,
                    eastAsia,
                    complexScript);
    }

    private static FontFamilies? Overlay(
        FontFamilies? inherited,
        FontFamilies? direct)
    {
        if (direct is null)
        {
            return inherited;
        }

        return new FontFamilies(
            Ascii: direct.Ascii ?? inherited?.Ascii,
            HighAnsi: direct.HighAnsi ?? inherited?.HighAnsi,
            EastAsia: direct.EastAsia ?? inherited?.EastAsia,
            ComplexScript:
                direct.ComplexScript ?? inherited?.ComplexScript);
    }

    private static Indentation? ReadIndentation(OpenXmlElement? indentation)
    {
        if (indentation is null)
        {
            return null;
        }

        Twip? left = OpenXmlValueReader.TwipAttribute(indentation, "left")
            ?? OpenXmlValueReader.TwipAttribute(indentation, "start");
        Twip? right = OpenXmlValueReader.TwipAttribute(indentation, "right")
            ?? OpenXmlValueReader.TwipAttribute(indentation, "end");
        Twip? firstLine =
            OpenXmlValueReader.TwipAttribute(indentation, "firstLine");
        Twip? hanging =
            OpenXmlValueReader.TwipAttribute(indentation, "hanging");

        return left is null
            && right is null
            && firstLine is null
            && hanging is null
                ? null
                : new Indentation(left, right, firstLine, hanging);
    }

    private static Indentation? Overlay(
        Indentation? inherited,
        Indentation? direct)
    {
        if (direct is null)
        {
            return inherited;
        }

        return new Indentation(
            Left: direct.Left ?? inherited?.Left,
            Right: direct.Right ?? inherited?.Right,
            FirstLine: direct.FirstLine ?? inherited?.FirstLine,
            Hanging: direct.Hanging ?? inherited?.Hanging);
    }

    private static LineSpacing? ReadLineSpacing(OpenXmlElement? spacing)
    {
        string? lineValue = OpenXmlValueReader.Attribute(spacing, "line");
        if (!long.TryParse(
                lineValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long parsed)
            || parsed <= 0)
        {
            return null;
        }

        return OpenXmlValueReader.Attribute(spacing, "lineRule") switch
        {
            "exact" => LineSpacing.Exact(new Twip(parsed)),
            "atLeast" => LineSpacing.AtLeast(new Twip(parsed)),
            null or "auto" when parsed <= int.MaxValue =>
                LineSpacing.Automatic(new LineMultiple((int)parsed)),
            _ => null,
        };
    }

    private static Twip? ReadHalfPointSize(OpenXmlElement? size)
    {
        string? value = OpenXmlValueReader.Attribute(size, "val");
        return long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long halfPoints)
            && halfPoints > 0
            && halfPoints <= long.MaxValue / 10
                ? new Twip(halfPoints * 10)
                : null;
    }

    private static Twip? ReadDxaWidth(OpenXmlElement? width) =>
        OpenXmlValueReader.Attribute(width, "type") is null or "dxa"
            ? OpenXmlValueReader.TwipAttribute(width, "w")
            : null;

    private static ParagraphAlignment? ReadParagraphAlignment(string? value) =>
        value switch
        {
            "left" or "start" => ParagraphAlignment.Left,
            "center" => ParagraphAlignment.Center,
            "right" or "end" => ParagraphAlignment.Right,
            "both" or "highKashida" or "mediumKashida" or "lowKashida" =>
                ParagraphAlignment.Justified,
            "distribute" or "thaiDistribute" =>
                ParagraphAlignment.Distributed,
            _ => null,
        };

    private static TableAlignment? ReadTableAlignment(string? value) =>
        value switch
        {
            "left" or "start" => TableAlignment.Left,
            "center" => TableAlignment.Center,
            "right" or "end" => TableAlignment.Right,
            _ => null,
        };

    private static CellVerticalAlignment? ReadCellVerticalAlignment(
        string? value) =>
        value switch
        {
            "top" => CellVerticalAlignment.Top,
            "center" => CellVerticalAlignment.Center,
            "bottom" => CellVerticalAlignment.Bottom,
            _ => null,
        };

    private static PageOrientation? ReadPageOrientation(string? value) =>
        value switch
        {
            "portrait" => PageOrientation.Portrait,
            "landscape" => PageOrientation.Landscape,
            _ => null,
        };

    private static Columns ReadColumns(OpenXmlElement? columns)
    {
        if (columns is null)
        {
            return new Columns(
                count: 1,
                equalWidth: true,
                separator: false);
        }

        var definitions = OpenXmlValueReader.Children(columns, "col")
            .Select(
                (column, index) => new ColumnDefinition(
                    index,
                    OpenXmlValueReader.TwipAttribute(column, "w"),
                    OpenXmlValueReader.TwipAttribute(column, "space")))
            .ToArray();

        return new Columns(
            count:
                OpenXmlValueReader.PositiveIntAttribute(columns, "num")
                ?? 1,
            spacing: OpenXmlValueReader.TwipAttribute(columns, "space"),
            equalWidth:
                OpenXmlValueReader.OnOffAttribute(columns, "equalWidth")
                ?? true,
            separator:
                OpenXmlValueReader.OnOffAttribute(columns, "sep")
                ?? false,
            definitions: definitions);
    }
}
