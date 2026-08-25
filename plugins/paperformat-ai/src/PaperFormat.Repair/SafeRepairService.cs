using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using PaperFormat.Checking;
using PaperFormat.Classification;
using PaperFormat.Domain;
using PaperFormat.Integrity;
using PaperFormat.OpenXml;

namespace PaperFormat.Repair;

/// <summary>
/// Copies an immutable source, applies selected allow-listed repairs, then
/// reopens, validates, rechecks, and verifies content integrity.
/// </summary>
public static class SafeRepairService
{
    public static RepairResult Execute(
        string sourcePath,
        string outputPath,
        RulePackage rules,
        CheckReport preRepairCheck,
        RepairSelection selection)
    {
        ValidateRequest(
            sourcePath,
            outputPath,
            rules,
            preRepairCheck,
            selection);
        string sourceFullPath = Path.GetFullPath(sourcePath);
        string outputFullPath = Path.GetFullPath(outputPath);
        string sourceHashBefore = FileHash(sourceFullPath);
        string outputDirectory = Path.GetDirectoryName(outputFullPath)
            ?? throw new ArgumentException(
                "The output path has no directory.",
                nameof(outputPath));
        Directory.CreateDirectory(outputDirectory);
        string temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileNameWithoutExtension(outputFullPath)}." +
            $"{Guid.NewGuid():N}.paperformat.tmp.docx");
        List<ChangeLogEntry> entries = [];

        try
        {
            File.Copy(sourceFullPath, temporaryPath, overwrite: false);
            ApplySelected(
                temporaryPath,
                rules,
                preRepairCheck,
                selection,
                entries);

            bool packageValid = HasNoNewPackageValidationErrors(
                sourceFullPath,
                temporaryPath);
            DocumentParseResult reopened =
                WordDocumentParser.Parse(temporaryPath);
            bool outputReopened = reopened.IsSuccess;
            DocumentModel outputDocument = reopened.Document
                ?? throw new InvalidDataException(
                    "The repaired DOCX could not be parsed after reopening.");
            ClassificationSet outputClassifications =
                new DeterministicDocumentClassifier().Classify(outputDocument);
            CheckReport postRepairCheck = new FormatCheckEngine().Check(
                outputDocument,
                rules,
                outputClassifications);
            IntegrityReport integrity = ContentIntegrityValidator.Compare(
                sourceFullPath,
                temporaryPath);
            string sourceHashAfter = FileHash(sourceFullPath);
            bool originalPreserved = string.Equals(
                sourceHashBefore,
                sourceHashAfter,
                StringComparison.Ordinal);
            string outputHash = FileHash(temporaryPath);
            string operationId = OperationId(
                sourceHashBefore,
                rules,
                selection);
            var changeLog = new ChangeLog(
                operationId,
                sourceHashBefore,
                outputHash,
                originalPreserved,
                outputReopened,
                packageValid,
                entries);
            bool allApplied = entries.All(
                entry => entry.Status == RepairExecutionStatus.Applied);
            bool ready = originalPreserved
                && outputReopened
                && packageValid
                && allApplied
                && integrity.Status == IntegrityStatus.Passed;

            File.Move(temporaryPath, outputFullPath);
            return new RepairResult(
                changeLog,
                integrity,
                postRepairCheck,
                ready);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private static void ApplySelected(
        string path,
        RulePackage rules,
        CheckReport report,
        RepairSelection selection,
        List<ChangeLogEntry> entries)
    {
        Dictionary<string, CheckIssue> issues = report.Issues
            .ToDictionary(issue => issue.IssueId, StringComparer.Ordinal);
        Dictionary<string, FormatRule> rulesById = rules.Rules
            .ToDictionary(rule => rule.RuleId, StringComparer.Ordinal);

        using WordprocessingDocument package =
            WordprocessingDocument.Open(path, true);
        MainDocumentPart main = package.MainDocumentPart
            ?? throw new InvalidDataException(
                "The package has no main document part.");
        var mutator = new OpenXmlFormatMutator(main, rules);

        foreach (string issueId in selection.IssueIds)
        {
            CheckIssue issue = issues[issueId];
            FormatRule rule = rulesById[issue.RuleId];
            (bool allowed, RepairAuthorization authorization, string reason) =
                Authorization(rule, issue.IssueId, selection);
            bool applied = allowed
                && mutator.Apply(rule, issue.DocumentLocation);
            RepairExecutionStatus status = !allowed
                ? RepairExecutionStatus.Skipped
                : applied
                    ? RepairExecutionStatus.Applied
                    : RepairExecutionStatus.Failed;
            string message = status switch
            {
                RepairExecutionStatus.Applied =>
                    "The approved deterministic format repair was applied.",
                RepairExecutionStatus.Skipped => reason,
                RepairExecutionStatus.Failed =>
                    "The approved target could not be safely resolved in the DOCX.",
                _ => throw new InvalidOperationException(
                    $"Unknown repair execution status '{status}'."),
            };
            entries.Add(new ChangeLogEntry(
                ChangeId(issue, rule, status),
                issue.IssueId,
                rule.RuleId,
                issue.ElementType,
                issue.DocumentLocation,
                rule.Property,
                issue.CurrentValue,
                rule.Expected,
                rule.Evidence,
                authorization,
                status,
                message));
        }

        main.Document!.Save();
        main.StyleDefinitionsPart?.Styles?.Save();
    }

    private static (
        bool Allowed,
        RepairAuthorization Authorization,
        string Reason) Authorization(
        FormatRule rule,
        string issueId,
        RepairSelection selection)
    {
        bool userConfirmed = selection.UserConfirmedIssueIds.Contains(
            issueId,
            StringComparer.Ordinal)
            || (rule.Target == RuleTarget.Page
                && selection.PageChangesConfirmed);
        if (!IsAllowListed(rule.Property))
        {
            return (
                false,
                RepairAuthorization.SafeAutomatic,
                "The property is outside the MVP repair allow-list.");
        }

        if (rule.Target == RuleTarget.Page)
        {
            return userConfirmed
                && selection.PageChangesConfirmed
                && rule.RepairLevel == RepairLevel.RequiresConfirmation
                ? (
                    true,
                    RepairAuthorization.UserConfirmed,
                    string.Empty)
                : (
                    false,
                    RepairAuthorization.UserConfirmed,
                    "Page changes require explicit confirmation.");
        }

        if (userConfirmed
            && rule.RepairLevel is
                RepairLevel.Safe
                or RepairLevel.RequiresConfirmation)
        {
            return (
                true,
                RepairAuthorization.UserConfirmed,
                string.Empty);
        }

        return rule.RepairLevel == RepairLevel.Safe
            ? (
                true,
                RepairAuthorization.SafeAutomatic,
                string.Empty)
            : (
                false,
                RepairAuthorization.SafeAutomatic,
                "The rule is not marked as a safe automatic repair.");
    }

    private static bool IsAllowListed(FormatProperty property) =>
        property is
            FormatProperty.PageWidth
            or FormatProperty.PageHeight
            or FormatProperty.PageOrientation
            or FormatProperty.MarginTop
            or FormatProperty.MarginRight
            or FormatProperty.MarginBottom
            or FormatProperty.MarginLeft
            or FormatProperty.FontAscii
            or FormatProperty.FontHighAnsi
            or FormatProperty.FontEastAsia
            or FormatProperty.FontComplexScript
            or FormatProperty.FontSize
            or FormatProperty.Bold
            or FormatProperty.Italic
            or FormatProperty.ParagraphAlignment
            or FormatProperty.LineSpacing
            or FormatProperty.SpaceBefore
            or FormatProperty.SpaceAfter
            or FormatProperty.FirstLineIndent
            or FormatProperty.ParagraphStyleId;

    private static bool HasNoNewPackageValidationErrors(
        string sourcePath,
        string outputPath)
    {
        Dictionary<PackageValidationSignature, int> baseline =
            ValidationErrors(sourcePath)
                .GroupBy(error => error)
                .ToDictionary(group => group.Key, group => group.Count());
        foreach (PackageValidationSignature outputError in
                 ValidationErrors(outputPath))
        {
            if (!baseline.TryGetValue(outputError, out int count)
                || count == 0)
            {
                return false;
            }

            baseline[outputError] = count - 1;
        }

        return true;
    }

    private static PackageValidationSignature[] ValidationErrors(
        string path)
    {
        using WordprocessingDocument package =
            WordprocessingDocument.Open(path, false);
        var validator = new OpenXmlValidator();
        return validator.Validate(package)
            .Select(
                error => new PackageValidationSignature(
                    error.Id ?? string.Empty,
                    error.Part?.Uri.ToString() ?? string.Empty,
                    error.Path?.XPath ?? string.Empty,
                    error.Node is null
                        ? string.Empty
                        : $"{error.Node.NamespaceUri}:{error.Node.LocalName}",
                    error.Description ?? string.Empty))
            .ToArray();
    }

    private static void ValidateRequest(
        string sourcePath,
        string outputPath,
        RulePackage rules,
        CheckReport report,
        RepairSelection selection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(selection);
        string sourceFullPath = Path.GetFullPath(sourcePath);
        string outputFullPath = Path.GetFullPath(outputPath);
        if (string.Equals(
                sourceFullPath,
                outputFullPath,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The repaired output must not overwrite the source.",
                nameof(outputPath));
        }

        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException(
                "The source DOCX does not exist.",
                sourceFullPath);
        }

        if (File.Exists(outputFullPath))
        {
            throw new IOException(
                "The repaired output path already exists.");
        }

        if (!string.Equals(
                Path.GetExtension(outputFullPath),
                ".docx",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The repaired output must use the .docx extension.",
                nameof(outputPath));
        }

        if (!string.Equals(
                report.RulePackageId,
                rules.PackageId,
                StringComparison.Ordinal)
            || report.RulePackageRevision != rules.Revision)
        {
            throw new ArgumentException(
                "The check report does not reference the supplied rule revision.",
                nameof(report));
        }

        HashSet<string> reportIssueIds = report.Issues
            .Select(issue => issue.IssueId)
            .ToHashSet(StringComparer.Ordinal);
        string? unknownIssue = selection.IssueIds.FirstOrDefault(
            issueId => !reportIssueIds.Contains(issueId));
        if (unknownIssue is not null)
        {
            throw new ArgumentException(
                $"The selected issue '{unknownIssue}' is not in the check report.",
                nameof(selection));
        }

        HashSet<string> ruleIds = rules.Rules
            .Select(rule => rule.RuleId)
            .ToHashSet(StringComparer.Ordinal);
        string? unknownRule = report.Issues
            .Where(issue => selection.IssueIds.Contains(issue.IssueId))
            .Select(issue => issue.RuleId)
            .FirstOrDefault(ruleId => !ruleIds.Contains(ruleId));
        if (unknownRule is not null)
        {
            throw new ArgumentException(
                $"The selected rule '{unknownRule}' is not in the rule package.",
                nameof(rules));
        }
    }

    private static string ChangeId(
        CheckIssue issue,
        FormatRule rule,
        RepairExecutionStatus status) =>
        "change-" + Hash(
            $"{issue.IssueId}|{rule.Property}|{status}")[..20];

    private static string OperationId(
        string sourceHash,
        RulePackage rules,
        RepairSelection selection) =>
        "repair-v1-" + Hash(
            string.Join(
                "|",
                sourceHash,
                rules.PackageId,
                rules.Revision,
                selection.PageChangesConfirmed,
                string.Join(",", selection.IssueIds)))[..20];

    private static string FileHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();

    private readonly record struct PackageValidationSignature(
        string Id,
        string PartUri,
        string Path,
        string NodeName,
        string Description);
}
