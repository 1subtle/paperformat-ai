using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using M = DocumentFormat.OpenXml.Math;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace PaperFormat.Fixtures;

internal static class DocumentFixtureBuilder
{
    private const string BodyStyleId = "BodyText";
    private const string TitleStyleId = "PaperTitle";
    private const string AuthorsStyleId = "Authors";
    private const string AffiliationStyleId = "Affiliation";
    private const string AbstractStyleId = "AbstractText";
    private const string KeywordsStyleId = "Keywords";
    private const string Heading1StyleId = "Heading1";
    private const string Heading2StyleId = "Heading2";
    private const string Heading3StyleId = "Heading3";
    private const string CaptionStyleId = "Caption";
    private const string TableTextStyleId = "TableText";

    private const string HyperlinkRelationshipId = "rIdHyperlink1";
    private const string ImageRelationshipId = "rIdImage1";
    private const string HeaderRelationshipId = "rIdHeader1";
    private const string FooterRelationshipId = "rIdFooter1";

    private const long ImageWidthEmu = 914_400L;
    private const long ImageHeightEmu = 457_200L;
    private const long WideImageWidthEmu = 5_486_400L;
    private const long WideImageHeightEmu = 2_743_200L;

    private static readonly DateTime FixedTimestamp =
        new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwC" +
        "AAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    public static void Build(string path, FixtureDefinition definition)
    {
        using var document = WordprocessingDocument.Create(
            path,
            WordprocessingDocumentType.Document,
            autoSave: true);

        AddFixedPackageProperties(document, definition);

        var mainPart = document.AddMainDocumentPart();
        document.ChangeIdOfPart(mainPart, "rIdDocument");

        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>("rIdStyles");
        stylesPart.Styles = BuildStyles(definition.HasWrongFormatting);

        var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>("rIdSettings");
        settingsPart.Settings = new W.Settings(
            new W.UpdateFieldsOnOpen { Val = false });

        AddNotesParts(mainPart, definition.Tag);
        AddHeaderAndFooterParts(mainPart, definition.Tag);
        AddImagePart(mainPart);
        mainPart.AddHyperlinkRelationship(
            new Uri("https://www.openai.com/", UriKind.Absolute),
            isExternal: true,
            HyperlinkRelationshipId);

        mainPart.Document = new W.Document(
            BuildBody(definition));

        mainPart.Document.Save();
        stylesPart.Styles.Save();
        settingsPart.Settings.Save();
    }

    private static void AddFixedPackageProperties(
        WordprocessingDocument document,
        FixtureDefinition definition)
    {
        var properties = document.PackageProperties;
        properties.Title = definition.Title;
        properties.Subject = "PaperFormat AI deterministic DOCX fixture";
        properties.Creator = "PaperFormat AI";
        properties.Keywords = $"paperformat,fixture,{definition.Tag}";
        properties.Description =
            "Synthetic OOXML package generated without Microsoft Word.";
        properties.LastModifiedBy = "PaperFormat AI";
        properties.Revision = "1";
        properties.Created = FixedTimestamp;
        properties.Modified = FixedTimestamp;
        properties.Category = "Test fixture";
        properties.ContentStatus = "Final";
        properties.Language = "en-US";
        properties.Version = "1.0";
    }

    private static W.Styles BuildStyles(bool wrongFormatting)
    {
        var defaultFont = wrongFormatting ? "Calibri" : "Times New Roman";
        var defaultSize = wrongFormatting ? 22 : 20;
        var bodyFont = wrongFormatting ? "Arial" : "Times New Roman";
        var bodySize = wrongFormatting ? 24 : 20;
        var bodyAfter = wrongFormatting ? 240 : 0;
        var bodyLine = wrongFormatting ? 360 : 240;

        return new W.Styles(
            new W.DocDefaults(
                new W.RunPropertiesDefault(
                    new W.RunPropertiesBaseStyle(
                        CreateRunFonts(defaultFont),
                        CreateFontSize(defaultSize),
                        new W.Languages { Val = "en-US" })),
                new W.ParagraphPropertiesDefault(
                    new W.ParagraphPropertiesBaseStyle(
                        CreateSpacing(0, 0, 240)))),
            CreateParagraphStyle(
                "Normal",
                "Normal",
                basedOn: null,
                defaultFont,
                defaultSize,
                W.JustificationValues.Left,
                beforeTwips: 0,
                afterTwips: 0,
                lineTwips: 240,
                isDefault: true),
            CreateParagraphStyle(
                BodyStyleId,
                "Body Text",
                "Normal",
                bodyFont,
                bodySize,
                wrongFormatting
                    ? W.JustificationValues.Center
                    : W.JustificationValues.Both,
                beforeTwips: 0,
                afterTwips: bodyAfter,
                lineTwips: bodyLine,
                firstLineTwips: wrongFormatting ? 0 : 289),
            CreateParagraphStyle(
                TitleStyleId,
                "Paper Title",
                "Normal",
                wrongFormatting ? "Arial" : "Times New Roman",
                wrongFormatting ? 32 : 48,
                wrongFormatting
                    ? W.JustificationValues.Left
                    : W.JustificationValues.Center,
                beforeTwips: 0,
                afterTwips: wrongFormatting ? 360 : 120,
                lineTwips: 240),
            CreateParagraphStyle(
                AuthorsStyleId,
                "Authors",
                "Normal",
                defaultFont,
                wrongFormatting ? 24 : 22,
                W.JustificationValues.Center,
                beforeTwips: 0,
                afterTwips: 60,
                lineTwips: 240),
            CreateParagraphStyle(
                AffiliationStyleId,
                "Affiliation",
                "Normal",
                defaultFont,
                wrongFormatting ? 22 : 18,
                W.JustificationValues.Center,
                beforeTwips: 0,
                afterTwips: 120,
                lineTwips: 240,
                italic: true),
            CreateParagraphStyle(
                AbstractStyleId,
                "Abstract Text",
                "Normal",
                bodyFont,
                wrongFormatting ? 22 : 18,
                W.JustificationValues.Both,
                beforeTwips: 0,
                afterTwips: 60,
                lineTwips: wrongFormatting ? 360 : 240),
            CreateParagraphStyle(
                KeywordsStyleId,
                "Keywords",
                "Normal",
                bodyFont,
                wrongFormatting ? 22 : 18,
                W.JustificationValues.Both,
                beforeTwips: 0,
                afterTwips: 120,
                lineTwips: 240),
            CreateParagraphStyle(
                Heading1StyleId,
                "Heading 1",
                "Normal",
                wrongFormatting ? "Arial" : "Times New Roman",
                wrongFormatting ? 28 : 20,
                wrongFormatting
                    ? W.JustificationValues.Left
                    : W.JustificationValues.Center,
                beforeTwips: wrongFormatting ? 240 : 120,
                afterTwips: 60,
                lineTwips: 240,
                bold: true,
                keepNext: true),
            CreateParagraphStyle(
                Heading2StyleId,
                "Heading 2",
                "Normal",
                defaultFont,
                wrongFormatting ? 24 : 20,
                W.JustificationValues.Left,
                beforeTwips: 120,
                afterTwips: 40,
                lineTwips: 240,
                italic: !wrongFormatting,
                keepNext: true),
            CreateParagraphStyle(
                Heading3StyleId,
                "Heading 3",
                "Normal",
                defaultFont,
                wrongFormatting ? 24 : 20,
                W.JustificationValues.Left,
                beforeTwips: 80,
                afterTwips: 40,
                lineTwips: 240,
                italic: !wrongFormatting,
                keepNext: true),
            CreateParagraphStyle(
                CaptionStyleId,
                "Caption",
                "Normal",
                wrongFormatting ? "Arial" : "Times New Roman",
                wrongFormatting ? 22 : 16,
                wrongFormatting
                    ? W.JustificationValues.Left
                    : W.JustificationValues.Center,
                beforeTwips: 40,
                afterTwips: 80,
                lineTwips: 240),
            CreateParagraphStyle(
                TableTextStyleId,
                "Table Text",
                "Normal",
                wrongFormatting ? "Arial" : "Times New Roman",
                wrongFormatting ? 22 : 16,
                W.JustificationValues.Left,
                beforeTwips: 0,
                afterTwips: 0,
                lineTwips: 240));
    }

    private static W.Style CreateParagraphStyle(
        string styleId,
        string name,
        string? basedOn,
        string fontName,
        int halfPointSize,
        W.JustificationValues alignment,
        int beforeTwips,
        int afterTwips,
        int lineTwips,
        bool isDefault = false,
        bool bold = false,
        bool italic = false,
        bool keepNext = false,
        int? firstLineTwips = null)
    {
        var paragraphProperties = new W.StyleParagraphProperties();
        if (keepNext)
        {
            paragraphProperties.Append(new W.KeepNext());
        }

        paragraphProperties.Append(
            CreateSpacing(beforeTwips, afterTwips, lineTwips));
        if (firstLineTwips is not null)
        {
            paragraphProperties.Append(
                new W.Indentation
                {
                    FirstLine = firstLineTwips.Value.ToString(
                        CultureInfo.InvariantCulture),
                });
        }
        paragraphProperties.Append(
            new W.Justification { Val = alignment });

        var runProperties = new W.StyleRunProperties(
            CreateRunFonts(fontName));
        if (bold)
        {
            runProperties.Append(new W.Bold());
        }

        if (italic)
        {
            runProperties.Append(new W.Italic());
        }

        runProperties.Append(
            CreateFontSize(halfPointSize),
            new W.Languages { Val = "en-US" });

        var style = new W.Style(
            new W.StyleName { Val = name })
        {
            Type = W.StyleValues.Paragraph,
            StyleId = styleId,
            Default = isDefault,
            CustomStyle = !isDefault,
        };

        if (basedOn is not null)
        {
            style.Append(
                new W.BasedOn { Val = basedOn },
                new W.NextParagraphStyle { Val = BodyStyleId });
        }

        style.Append(
            new W.UIPriority { Val = isDefault ? 0 : 10 },
            new W.PrimaryStyle(),
            paragraphProperties,
            runProperties);

        return style;
    }

    private static W.RunFonts CreateRunFonts(string fontName) =>
        new()
        {
            Ascii = fontName,
            HighAnsi = fontName,
            EastAsia = fontName,
            ComplexScript = fontName,
        };

    private static W.FontSize CreateFontSize(int halfPointSize) =>
        new()
        {
            Val = halfPointSize.ToString(CultureInfo.InvariantCulture),
        };

    private static W.SpacingBetweenLines CreateSpacing(
        int beforeTwips,
        int afterTwips,
        int lineTwips) =>
        new()
        {
            Before = beforeTwips.ToString(CultureInfo.InvariantCulture),
            After = afterTwips.ToString(CultureInfo.InvariantCulture),
            Line = lineTwips.ToString(CultureInfo.InvariantCulture),
            LineRule = W.LineSpacingRuleValues.Auto,
        };

    private static void AddNotesParts(MainDocumentPart mainPart, string tag)
    {
        var footnotesPart =
            mainPart.AddNewPart<FootnotesPart>("rIdFootnotes1");
        footnotesPart.Footnotes = new W.Footnotes(
            new W.Footnote(
                new W.Paragraph(
                    new W.Run(
                        new W.SeparatorMark())))
            {
                Id = -1,
            },
            new W.Footnote(
                new W.Paragraph(
                    new W.Run(
                        new W.ContinuationSeparatorMark())))
            {
                Id = 0,
            },
            new W.Footnote(
                new W.Paragraph(
                    new W.Run(
                        new W.FootnoteReferenceMark()),
                    new W.Run(
                        new W.Text(
                            $"Fixed footnote content for {tag}."))))
            {
                Id = 1,
            });
        footnotesPart.Footnotes.Save();

        var endnotesPart =
            mainPart.AddNewPart<EndnotesPart>("rIdEndnotes1");
        endnotesPart.Endnotes = new W.Endnotes(
            new W.Endnote(
                new W.Paragraph(
                    new W.Run(
                        new W.SeparatorMark())))
            {
                Id = -1,
            },
            new W.Endnote(
                new W.Paragraph(
                    new W.Run(
                        new W.ContinuationSeparatorMark())))
            {
                Id = 0,
            },
            new W.Endnote(
                new W.Paragraph(
                    new W.Run(
                        new W.EndnoteReferenceMark()),
                    new W.Run(
                        new W.Text(
                            $"Fixed endnote content for {tag}."))))
            {
                Id = 1,
            });
        endnotesPart.Endnotes.Save();
    }

    private static void AddHeaderAndFooterParts(
        MainDocumentPart mainPart,
        string tag)
    {
        var headerPart = mainPart.AddNewPart<HeaderPart>(HeaderRelationshipId);
        headerPart.Header = new W.Header(
            new W.Paragraph(
                new W.Run(
                    new W.Text($"PaperFormat fixture header — {tag}"))));
        headerPart.Header.Save();

        var footerPart = mainPart.AddNewPart<FooterPart>(FooterRelationshipId);
        footerPart.Footer = new W.Footer(
            new W.Paragraph(
                new W.Run(
                    new W.Text("Deterministic footer"))));
        footerPart.Footer.Save();
    }

    private static void AddImagePart(MainDocumentPart mainPart)
    {
        var imagePart = mainPart.AddImagePart(
            ImagePartType.Png,
            ImageRelationshipId);
        using var imageStream = new MemoryStream(PngBytes, writable: false);
        imagePart.FeedData(imageStream);
    }

    private static W.Body BuildBody(FixtureDefinition definition)
    {
        var body = new W.Body(
            CreateParagraph(TitleStyleId, definition.Title),
            CreateParagraph(AuthorsStyleId, "Alice Example and Bob Example"),
            CreateParagraph(
                AffiliationStyleId,
                "Department of Reproducible Documents, Example University"),
            CreateParagraph(
                AbstractStyleId,
                "Abstract—This synthetic manuscript exercises deterministic " +
                "DOCX parsing without changing scholarly content."),
            CreateParagraph(
                KeywordsStyleId,
                "Index Terms—DOCX, OOXML, deterministic fixtures."),
            CreateParagraph(Heading1StyleId, "I. INTRODUCTION"),
            CreateRichContentParagraph(definition),
            CreateParagraph(
                Heading2StyleId,
                "A. Deterministic Construction"),
            CreateParagraph(
                BodyStyleId,
                "Each package uses fixed metadata, stable relationships, and " +
                "a canonical ZIP entry order.",
                definition.HasWrongFormatting),
            CreateParagraph(Heading3StyleId, "1) Package Contents"),
            CreateParagraph(
                BodyStyleId,
                "The document contains paragraphs, a table, notes, a field, " +
                "an image, and an Office Math equation."),
            CreateEquationParagraph(),
            CreateImageParagraph(definition.HasWideLayoutObjects),
            CreateParagraph(
                CaptionStyleId,
                "Fig. 1. A deterministic one-pixel fixture image."),
            CreateParagraph(
                CaptionStyleId,
                "TABLE I. FIXTURE CONTENT"),
            CreateTable(
                definition.HasWrongFormatting,
                definition.HasWideLayoutObjects),
            BuildSectionProperties(
                definition.HasWrongFormatting,
                definition.IsSingleColumn));

        W.Paragraph equation = body.Elements<W.Paragraph>()
            .Single(item => item.Descendants<M.OfficeMath>().Any());
        W.SectionProperties finalSection = body
            .Elements<W.SectionProperties>()
            .Single();
        int leadingParagraphs =
            (definition.ExtraBodyParagraphCount + 1) / 2;
        for (int index = 0;
             index < definition.ExtraBodyParagraphCount;
             index++)
        {
            W.Paragraph paragraph = CreateParagraph(
                BodyStyleId,
                "This additional deterministic body paragraph verifies " +
                "that content flows across the target column boundary " +
                $"without changing document resources ({index + 1}).");
            body.InsertBefore(
                paragraph,
                index < leadingParagraphs
                    ? equation
                    : finalSection);
        }

        return body;
    }

    private static W.Paragraph CreateParagraph(
        string styleId,
        string text,
        bool addWrongDirectFormatting = false)
    {
        var paragraphProperties = new W.ParagraphProperties(
            new W.ParagraphStyleId { Val = styleId });
        var run = new W.Run();

        if (addWrongDirectFormatting)
        {
            paragraphProperties.Append(
                CreateSpacing(0, 360, 480),
                new W.Justification { Val = W.JustificationValues.Right });
            run.RunProperties = new W.RunProperties(
                CreateRunFonts("Courier New"),
                new W.Bold(),
                CreateFontSize(28));
        }

        run.Append(new W.Text(text));
        return new W.Paragraph(paragraphProperties, run);
    }

    private static W.Paragraph CreateRichContentParagraph(
        FixtureDefinition definition)
    {
        var paragraph = new W.Paragraph(
            new W.ParagraphProperties(
                new W.ParagraphStyleId { Val = BodyStyleId }),
            new W.Run(
                new W.Text("This fixture preserves a ")),
            new W.BookmarkStart
            {
                Id = "1",
                Name = "FixtureBookmark",
            },
            new W.Run(
                new W.Text("stable bookmark")),
            new W.BookmarkEnd { Id = "1" },
            new W.Run(
                new W.Text(", an external ")),
            new W.Hyperlink(
                new W.Run(
                    new W.RunProperties(
                        new W.Color { Val = "0563C1" },
                        new W.Underline
                        {
                            Val = W.UnderlineValues.Single,
                        }),
                    new W.Text("OpenAI link")))
            {
                Id = HyperlinkRelationshipId,
                History = true,
            },
            new W.Run(
                new W.Text(", a REF field (")),
            new W.Run(
                new W.FieldChar
                {
                    FieldCharType = W.FieldCharValues.Begin,
                }),
            new W.Run(
                new W.FieldCode(" REF FixtureBookmark \\h ")
                {
                    Space = SpaceProcessingModeValues.Preserve,
                }),
            new W.Run(
                new W.FieldChar
                {
                    FieldCharType = W.FieldCharValues.Separate,
                }),
            new W.Run(
                new W.Text("stable bookmark")),
            new W.Run(
                new W.FieldChar
                {
                    FieldCharType = W.FieldCharValues.End,
                }),
            new W.Run(
                new W.Text("), a footnote")),
            new W.Run(
                new W.FootnoteReference { Id = 1 }),
            new W.Run(
                new W.Text(", and an endnote")),
            new W.Run(
                new W.EndnoteReference { Id = 1 }),
            new W.Run(
                new W.Text($". Fixture tag: {definition.Tag}.")));

        if (!definition.IncludeNoteReferences)
        {
            paragraph.Descendants<W.FootnoteReference>()
                .ToList()
                .ForEach(item => item.Remove());
            paragraph.Descendants<W.EndnoteReference>()
                .ToList()
                .ForEach(item => item.Remove());
        }

        if (definition.HasWrongFormatting)
        {
            paragraph.ParagraphProperties!.Append(
                CreateSpacing(0, 240, 360),
                new W.Justification { Val = W.JustificationValues.Center });
        }

        return paragraph;
    }

    private static W.Paragraph CreateEquationParagraph() =>
        new(
            new W.ParagraphProperties(
                new W.ParagraphStyleId { Val = BodyStyleId },
                new W.Justification
                {
                    Val = W.JustificationValues.Center,
                }),
            new M.OfficeMath(
                new M.Run(
                    new M.Text("E = mc²"))));

    private static W.Paragraph CreateImageParagraph(bool wide)
    {
        long width = wide ? WideImageWidthEmu : ImageWidthEmu;
        long height = wide ? WideImageHeightEmu : ImageHeightEmu;
        return
        new(
            new W.ParagraphProperties(
                new W.ParagraphStyleId { Val = BodyStyleId },
                new W.Justification
                {
                    Val = W.JustificationValues.Center,
                }),
            new W.Run(
                new W.Drawing(
                    new DW.Inline(
                        new DW.Extent
                        {
                            Cx = width,
                            Cy = height,
                        },
                        new DW.EffectExtent
                        {
                            LeftEdge = 0L,
                            TopEdge = 0L,
                            RightEdge = 0L,
                            BottomEdge = 0L,
                        },
                        new DW.DocProperties
                        {
                            Id = 1U,
                            Name = "Fixture image",
                            Description =
                                "Synthetic one-pixel PNG for parser tests",
                        },
                        new DW.NonVisualGraphicFrameDrawingProperties(
                            new A.GraphicFrameLocks
                            {
                                NoChangeAspect = true,
                            }),
                        new A.Graphic(
                            new A.GraphicData(
                                new PIC.Picture(
                                    new PIC.NonVisualPictureProperties(
                                        new PIC.NonVisualDrawingProperties
                                        {
                                            Id = 0U,
                                            Name = "fixture.png",
                                        },
                                        new PIC
                                            .NonVisualPictureDrawingProperties()),
                                    new PIC.BlipFill(
                                        new A.Blip
                                        {
                                            Embed = ImageRelationshipId,
                                            CompressionState =
                                                A.BlipCompressionValues.Print,
                                        },
                                        new A.Stretch(
                                            new A.FillRectangle())),
                                    new PIC.ShapeProperties(
                                        new A.Transform2D(
                                            new A.Offset
                                            {
                                                X = 0L,
                                                Y = 0L,
                                            },
                                            new A.Extents
                                            {
                                                Cx = width,
                                                Cy = height,
                                            }),
                                        new A.PresetGeometry(
                                            new A.AdjustValueList())
                                        {
                                            Preset =
                                                A.ShapeTypeValues.Rectangle,
                                        })))
                            {
                                Uri =
                                    "http://schemas.openxmlformats.org/" +
                                    "drawingml/2006/picture",
                            }))
                    {
                        DistanceFromTop = 0U,
                        DistanceFromBottom = 0U,
                        DistanceFromLeft = 0U,
                        DistanceFromRight = 0U,
                    })));
    }

    private static W.Table CreateTable(
        bool wrongFormatting,
        bool wide)
    {
        string cellWidth = wide ? "3600" : "2600";
        string tableWidth = wide ? "7200" : "0";
        var table = new W.Table(
            new W.TableProperties(
                new W.TableStyle { Val = "TableGrid" },
                new W.TableWidth
                {
                    Width = tableWidth,
                    Type = wide
                        ? W.TableWidthUnitValues.Dxa
                        : W.TableWidthUnitValues.Auto,
                },
                new W.TableLayout
                {
                    Type = W.TableLayoutValues.Fixed,
                }),
            new W.TableGrid(
                new W.GridColumn { Width = cellWidth },
                new W.GridColumn { Width = cellWidth }));

        table.Append(
            new W.TableRow(
                new W.TableRowProperties(
                    new W.TableHeader()),
                CreateTableCell(
                    "Object",
                    isHeader: true,
                    wrongFormatting,
                    cellWidth),
                CreateTableCell(
                    "Expected",
                    isHeader: true,
                    wrongFormatting,
                    cellWidth)),
            new W.TableRow(
                CreateTableCell(
                    "Paragraphs",
                    false,
                    wrongFormatting,
                    cellWidth),
                CreateTableCell(
                    "Stable",
                    false,
                    wrongFormatting,
                    cellWidth)),
            new W.TableRow(
                CreateTableCell(
                    "Relationships",
                    false,
                    wrongFormatting,
                    cellWidth),
                CreateTableCell(
                    "Fixed IDs",
                    false,
                    wrongFormatting,
                    cellWidth)));

        return table;
    }

    private static W.TableCell CreateTableCell(
        string text,
        bool isHeader,
        bool wrongFormatting,
        string cellWidth)
    {
        var runProperties = new W.RunProperties();
        if (wrongFormatting)
        {
            runProperties.Append(CreateRunFonts("Arial"));
        }

        if (isHeader)
        {
            runProperties.Append(new W.Bold());
        }

        if (wrongFormatting)
        {
            runProperties.Append(CreateFontSize(24));
        }

        return new W.TableCell(
            new W.TableCellProperties(
                new W.TableCellWidth
                {
                    Width = cellWidth,
                    Type = W.TableWidthUnitValues.Dxa,
                }),
            new W.Paragraph(
                new W.ParagraphProperties(
                    new W.ParagraphStyleId
                    {
                        Val = TableTextStyleId,
                    }),
                new W.Run(
                    runProperties,
                    new W.Text(text))));
    }

    private static W.SectionProperties BuildSectionProperties(
        bool wrongFormatting,
        bool singleColumn)
    {
        if (wrongFormatting)
        {
            return new W.SectionProperties(
                new W.HeaderReference
                {
                    Type = W.HeaderFooterValues.Default,
                    Id = HeaderRelationshipId,
                },
                new W.FooterReference
                {
                    Type = W.HeaderFooterValues.Default,
                    Id = FooterRelationshipId,
                },
                new W.PageSize
                {
                    Width = 16_838U,
                    Height = 11_906U,
                    Orient = W.PageOrientationValues.Landscape,
                },
                new W.PageMargin
                {
                    Top = 1_440,
                    Right = 1_440U,
                    Bottom = 1_440,
                    Left = 1_440U,
                    Header = 720U,
                    Footer = 720U,
                    Gutter = 0U,
                },
                new W.Columns
                {
                    EqualWidth = true,
                    ColumnCount = 1,
                    Space = "720",
                });
        }

        return new W.SectionProperties(
            new W.HeaderReference
            {
                Type = W.HeaderFooterValues.Default,
                Id = HeaderRelationshipId,
            },
            new W.FooterReference
            {
                Type = W.HeaderFooterValues.Default,
                Id = FooterRelationshipId,
            },
            new W.PageSize
            {
                Width = 12_240U,
                Height = 15_840U,
                Orient = W.PageOrientationValues.Portrait,
            },
            new W.PageMargin
            {
                Top = 1_080,
                Right = 720U,
                Bottom = 1_080,
                Left = 720U,
                Header = 360U,
                Footer = 360U,
                Gutter = 0U,
            },
            new W.Columns
            {
                EqualWidth = true,
                ColumnCount = singleColumn ? (short)1 : (short)2,
                Space = "360",
            });
    }
}
