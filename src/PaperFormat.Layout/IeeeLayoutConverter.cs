using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using PaperFormat.Domain;
using PaperFormat.OpenXml;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace PaperFormat.Layout;

public sealed record LayoutExecutionResult(
    LayoutChangeLog ChangeLog,
    bool PackageValid,
    bool Reopened);

/// <summary>
/// Transactionally applies approved Review layout operations to a candidate
/// copy. The caller remains responsible for integrity, recheck, and rendering.
/// </summary>
public static class IeeeLayoutConverter
{
    public static LayoutExecutionResult Apply(
        string candidatePath,
        IReadOnlyList<LayoutOperation> operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentNullException.ThrowIfNull(operations);
        string fullPath = Path.GetFullPath(candidatePath);
        DocumentModel sourceDocument = WordDocumentParser.Parse(fullPath)
            .Document
            ?? throw new InvalidDataException(
                "The layout source could not be parsed.");
        string hashBefore = Hash(fullPath);
        string temporary = Path.Combine(
            Path.GetDirectoryName(fullPath)
                ?? throw new ArgumentException(
                    "The candidate path has no directory.",
                    nameof(candidatePath)),
            $".{Path.GetFileNameWithoutExtension(fullPath)}." +
            $"{Guid.NewGuid():N}.layout.tmp.docx");
        File.Copy(fullPath, temporary, overwrite: false);
        var entries = new List<LayoutChangeEntry>();
        try
        {
            using (WordprocessingDocument package =
                   WordprocessingDocument.Open(temporary, true))
            {
                W.Body body = package.MainDocumentPart?.Document?.Body
                    ?? throw new InvalidDataException(
                        "The candidate has no main document body.");
                foreach (LayoutOperation operation in operations)
                {
                    if (operation.Decision != RepairPlanDecision.Apply
                        || operation.Level != ModificationLevel.Review)
                    {
                        throw new InvalidOperationException(
                            $"Layout operation '{operation.OperationId}' was not approved as Review.");
                    }

                    bool applied = operation.Kind switch
                    {
                        LayoutOperationKind.InsertContinuousSectionBreak =>
                            InsertSectionBreak(
                                body,
                                operation.AfterElementId!,
                                W.SectionMarkValues.Continuous),
                        LayoutOperationKind.InsertNextPageSectionBreak =>
                            InsertSectionBreak(
                                body,
                                operation.AfterElementId!,
                                W.SectionMarkValues.NextPage),
                        LayoutOperationKind.SetSectionColumns =>
                            SetColumns(
                                body,
                                operation.TargetSectionIndex!.Value,
                                operation.ColumnCount!.Value,
                                operation.ColumnSpacingTwips!.Value),
                        _ => false,
                    };
                    entries.Add(
                        new LayoutChangeEntry(
                            operation.OperationId,
                            operation.Kind,
                            applied
                                ? RepairExecutionStatus.Applied
                                : RepairExecutionStatus.Failed,
                            applied
                                ? "The approved layout operation was applied to the candidate copy."
                                : "The approved layout target could not be resolved."));
                    if (!applied)
                    {
                        throw new InvalidOperationException(
                            $"Layout operation '{operation.OperationId}' failed.");
                    }
                }

                package.MainDocumentPart!.Document.Save();
            }

            string[] newValidationErrors = NewValidationErrors(
                fullPath,
                temporary);
            bool valid = newValidationErrors.Length == 0;
            DocumentParseResult reopenedParse =
                WordDocumentParser.Parse(temporary);
            bool reopened = reopenedParse.IsSuccess;
            bool layoutMatches = reopenedParse.Document is { } outputDocument
                && MatchesApprovedLayout(
                    sourceDocument,
                    outputDocument,
                    operations);
            if (!valid || !reopened || !layoutMatches)
            {
                throw new InvalidDataException(
                    "The layout candidate failed OOXML or reopen validation. "
                    + $"Reopened: {reopened}. "
                    + $"Layout matches plan: {layoutMatches}. "
                    + "New validation errors: "
                    + (newValidationErrors.Length == 0
                        ? "none"
                        : string.Join(
                            " || ",
                            newValidationErrors.Take(3))));
            }

            string hashAfter = Hash(temporary);
            File.Move(temporary, fullPath, overwrite: true);
            return new LayoutExecutionResult(
                new LayoutChangeLog(
                    LayoutChangeLog.CurrentSchemaVersion,
                    hashBefore,
                    hashAfter,
                    entries),
                valid,
                reopened);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    private static bool InsertSectionBreak(
        W.Body body,
        string afterElementId,
        W.SectionMarkValues sectionStart)
    {
        (int sectionIndex, int paragraphIndex) =
            ParseParagraphElementId(afterElementId);
        W.Paragraph? paragraph = Paragraph(
            body,
            sectionIndex,
            paragraphIndex);
        if (paragraph is null)
        {
            return false;
        }

        W.ParagraphProperties properties =
            paragraph.ParagraphProperties
            ?? paragraph.PrependChild(new W.ParagraphProperties());
        if (properties.SectionProperties is not null)
        {
            W.SectionProperties existing =
                properties.SectionProperties;
            SetColumns(existing, 1, 0);
            W.SectionProperties? following = body
                .Descendants<W.SectionProperties>()
                .SkipWhile(item => !ReferenceEquals(item, existing))
                .Skip(1)
                .FirstOrDefault();
            if (following is null)
            {
                return false;
            }

            SetSectionStart(following, sectionStart);
            return true;
        }

        W.SectionProperties source =
            body.Elements<W.SectionProperties>().LastOrDefault()
            ?? new W.SectionProperties();
        W.SectionProperties first =
            (W.SectionProperties)source.CloneNode(true);
        SetColumns(first, 1, 0);
        first.RemoveAllChildren<W.SectionType>();
        properties.Append(first);
        SetSectionStart(source, sectionStart);
        return true;
    }

    private static bool SetColumns(
        W.Body body,
        int sectionIndex,
        int count,
        int spacing)
    {
        W.SectionProperties[] sections = body
            .Descendants<W.SectionProperties>()
            .Concat(body.Elements<W.SectionProperties>())
            .Distinct()
            .ToArray();
        if (sectionIndex < 0 || sectionIndex >= sections.Length)
        {
            return false;
        }

        SetColumns(sections[sectionIndex], count, spacing);
        return true;
    }

    private static void SetColumns(
        W.SectionProperties section,
        int count,
        int spacing)
    {
        W.Columns columns = section.GetFirstChild<W.Columns>()
            ?? section.AppendChild(new W.Columns());
        columns.ColumnCount = checked((short)count);
        columns.Space = spacing.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        columns.EqualWidth = true;
        columns.Separator = false;
        columns.RemoveAllChildren<W.Column>();
    }

    private static void SetSectionStart(
        W.SectionProperties section,
        W.SectionMarkValues sectionStart)
    {
        W.SectionType? type = section.GetFirstChild<W.SectionType>();
        if (type is null)
        {
            type = new W.SectionType();
            OpenXmlElement? pageSize =
                section.GetFirstChild<W.PageSize>();
            if (pageSize is null)
            {
                section.Append(type);
            }
            else
            {
                section.InsertBefore(type, pageSize);
            }
        }

        type.Val = sectionStart;
    }

    private static W.Paragraph? Paragraph(
        W.Body body,
        int targetSection,
        int targetParagraph)
    {
        int section = 0;
        int paragraph = 0;
        foreach (OpenXmlElement child in body.ChildElements)
        {
            if (child is not W.Paragraph item)
            {
                continue;
            }

            if (section == targetSection
                && paragraph == targetParagraph)
            {
                return item;
            }

            paragraph++;
            if (item.ParagraphProperties?.SectionProperties is not null)
            {
                section++;
                paragraph = 0;
            }
        }

        return null;
    }

    private static (int Section, int Paragraph) ParseParagraphElementId(
        string elementId)
    {
        const string prefix = "element:main/section[";
        if (!elementId.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The layout boundary is not a main-document paragraph element.");
        }

        int sectionEnd = elementId.IndexOf(']', prefix.Length);
        const string paragraphToken = "/paragraph[";
        int paragraphStart = elementId.IndexOf(
            paragraphToken,
            sectionEnd + 1,
            StringComparison.Ordinal);
        int paragraphEnd = elementId.IndexOf(
            ']',
            paragraphStart + paragraphToken.Length);
        if (sectionEnd < 0
            || paragraphStart < 0
            || paragraphEnd < 0
            || !int.TryParse(
                elementId[prefix.Length..sectionEnd],
                out int section)
            || !int.TryParse(
                elementId[(paragraphStart + paragraphToken.Length)..paragraphEnd],
                out int paragraph))
        {
            throw new InvalidDataException(
                "The layout boundary element identifier is invalid.");
        }

        return (section, paragraph);
    }

    private static string[] NewValidationErrors(
        string source,
        string output)
    {
        Dictionary<string, int> baseline = ValidationErrors(source)
            .GroupBy(item => item, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        var newErrors = new List<string>();
        foreach (string error in ValidationErrors(output))
        {
            if (!baseline.TryGetValue(error, out int count)
                || count == 0)
            {
                newErrors.Add(error);
                continue;
            }

            baseline[error] = count - 1;
        }

        return newErrors.ToArray();
    }

    private static bool MatchesApprovedLayout(
        DocumentModel source,
        DocumentModel output,
        IReadOnlyList<LayoutOperation> operations)
    {
        int inserted = operations.Count(
            item => item.Kind is
                LayoutOperationKind.InsertContinuousSectionBreak
                or LayoutOperationKind.InsertNextPageSectionBreak);
        if (output.Sections.Count != source.Sections.Count + inserted)
        {
            return false;
        }

        if (inserted > 0
            && output.Sections[0].PageSettings.Columns.Count != 1)
        {
            return false;
        }

        foreach (LayoutOperation operation in operations.Where(
                     item => item.Kind
                         == LayoutOperationKind.SetSectionColumns))
        {
            int sectionIndex = operation.TargetSectionIndex!.Value;
            if (sectionIndex >= output.Sections.Count)
            {
                return false;
            }

            Columns actual =
                output.Sections[sectionIndex].PageSettings.Columns;
            if (actual.Count != operation.ColumnCount
                || actual.Spacing?.Value
                    != operation.ColumnSpacingTwips)
            {
                return false;
            }
        }

        return true;
    }

    private static string[] ValidationErrors(string path)
    {
        using WordprocessingDocument package =
            WordprocessingDocument.Open(path, false);
        return new OpenXmlValidator()
            .Validate(package)
            .Select(
                item =>
                    $"{item.Id}|{item.Part?.Uri}|" +
                    $"{item.Node?.NamespaceUri}:{item.Node?.LocalName}|" +
                    $"{item.Description}")
            .ToArray();
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
    }
}
