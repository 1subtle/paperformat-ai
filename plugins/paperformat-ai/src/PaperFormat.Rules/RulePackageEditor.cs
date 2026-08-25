using PaperFormat.Domain;

namespace PaperFormat.Rules;

/// <summary>
/// Applies explicit user overrides without mutating the extracted package.
/// </summary>
public static class RulePackageEditor
{
    public static RulePackage Apply(
        RulePackage package,
        IEnumerable<RuleOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(overrides);

        RuleOverride[] edits = overrides.ToArray();
        string? duplicateRuleId = edits
            .GroupBy(edit => edit.RuleId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateRuleId is not null)
        {
            throw new ArgumentException(
                "A rule can be overridden at most once per revision.",
                nameof(overrides));
        }

        var editsById = edits.ToDictionary(
            edit => edit.RuleId,
            StringComparer.Ordinal);
        string? unknownRuleId = editsById.Keys
            .FirstOrDefault(
                ruleId => package.Rules.All(
                    rule => !string.Equals(
                        rule.RuleId,
                        ruleId,
                        StringComparison.Ordinal)));
        if (unknownRuleId is not null)
        {
            throw new ArgumentException(
                $"The rule '{unknownRuleId}' does not exist in the package.",
                nameof(overrides));
        }

        FormatRule[] rules = package.Rules
            .Select(
                rule => editsById.TryGetValue(
                    rule.RuleId,
                    out RuleOverride? edit)
                    ? Apply(rule, edit)
                    : rule)
            .ToArray();

        return new RulePackage(
            package.PackageId,
            package.Revision + 1,
            package.Name,
            package.ProviderId,
            package.ProviderVersion,
            package.SourceReference,
            rules,
            package.Notices,
            package.SchemaVersion);
    }

    private static FormatRule Apply(FormatRule rule, RuleOverride edit) =>
        new(
            rule.RuleId,
            rule.Target,
            rule.Property,
            edit.Expected ?? rule.Expected,
            rule.Severity,
            rule.RepairLevel,
            new RuleEvidence(
                RuleEvidenceKind.UserOverride,
                "user",
                $"override:{rule.RuleId}"),
            confidence: 1m,
            enabled: edit.Enabled ?? rule.Enabled,
            needsConfirmation: false);
}
