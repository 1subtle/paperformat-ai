using System.Security.Cryptography;
using System.Text;
using PaperFormat.Domain;

namespace PaperFormat.Ai;

/// <summary>
/// Converts untrusted AI rule decisions into a fail-closed repair plan.
/// </summary>
public static class RepairPlanPolicy
{
    public static IReadOnlyList<RepairPlanCandidateGroup> CreateCandidateGroups(
        CheckReport report,
        RulePackage rules)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(rules);
        Dictionary<string, FormatRule> ruleMap = rules.Rules
            .ToDictionary(item => item.RuleId, StringComparer.Ordinal);
        return report.Issues
            .GroupBy(
                issue =>
                {
                    FormatRule rule = ruleMap[issue.RuleId];
                    return (
                        issue.RuleId,
                        ScopeKey(rule, issue));
                })
            .OrderBy(group => group.Key.RuleId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Item2, StringComparer.Ordinal)
            .Select(
                group =>
                {
                    FormatRule rule = ruleMap[group.Key.RuleId];
                    CheckIssue[] issues = group
                        .OrderBy(item => item.DocumentLocation)
                        .ThenBy(item => item.IssueId, StringComparer.Ordinal)
                        .ToArray();
                    string groupId = Id(
                        report.ReportId,
                        group.Key.RuleId,
                        group.Key.Item2);
                    return new RepairPlanCandidateGroup(
                        groupId,
                        group.Key.RuleId,
                        ScopeLabel(rule, issues[0]),
                        issues.Select(item => item.IssueId).ToArray(),
                        issues.Select(item => item.DocumentLocation).ToArray());
                })
            .ToArray();
    }

    public static RepairPlan Validate(
        CheckReport report,
        RulePackage rules,
        IEnumerable<ProposedDirective> proposed,
        string providerId,
        string model,
        RepairPlanOrigin origin,
        bool visualEvidenceUsed,
        bool externalProcessingConsent,
        string sourceSha256,
        IEnumerable<ProposedLayoutOperation>? proposedLayoutOperations = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(proposed);

        Dictionary<string, FormatRule> ruleMap = rules.Rules
            .ToDictionary(item => item.RuleId, StringComparer.Ordinal);
        Dictionary<string, CheckIssue> issueMap = report.Issues
            .ToDictionary(item => item.IssueId, StringComparer.Ordinal);
        RepairPlanCandidateGroup[] groups =
            CreateCandidateGroups(report, rules).ToArray();
        Dictionary<string, RepairPlanCandidateGroup> groupMap = groups
            .ToDictionary(
                group => group.GroupId,
                StringComparer.Ordinal);
        ProposedDirective[] proposalItems = proposed.ToArray();
        IGrouping<string, ProposedDirective>[] proposalGroups = proposalItems
            .Where(item => groupMap.ContainsKey(item.GroupId))
            .GroupBy(item => item.GroupId, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, ProposedDirective> proposals = proposalGroups
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        List<string> notices = proposalItems
            .Where(item => !groupMap.ContainsKey(item.GroupId))
            .Select(
                item =>
                    $"Ignored AI directive for unknown or already-passing scope '{item.GroupId}'.")
            .Concat(
                proposalGroups
                    .Where(group => group.Count() > 1)
                    .Select(
                        group =>
                            $"Rejected duplicate AI directives for scope '{group.Key}'."))
            .ToList();
        List<RepairPlanDirective> directives = [];

        foreach (RepairPlanCandidateGroup group in groups)
        {
            FormatRule rule = ruleMap[group.RuleId];
            CheckIssue[] issues = group.IssueIds
                .Select(issueId => issueMap[issueId])
                .ToArray();
            ProposedDirective candidate = proposals.TryGetValue(
                group.GroupId,
                out ProposedDirective? value)
                ? value
                : new ProposedDirective(
                    group.GroupId,
                    RepairPlanDecision.ReportOnly,
                    RepairPlanRisk.High,
                    0m,
                    "The AI response omitted this protected scope.");
            List<string> safetyNotes = [];
            RepairPlanDecision decision = candidate.Decision;
            RepairPlanRisk risk = candidate.Risk;
            decimal confidence = Math.Clamp(candidate.Confidence, 0m, 1m);
            string rollbackStrategy = AllowedRollback(
                candidate.RollbackStrategy)
                ? candidate.RollbackStrategy
                : "discardCandidateCopy";
            if (!AllowedRollback(candidate.RollbackStrategy))
            {
                safetyNotes.Add(
                    "Unknown rollback strategy was replaced with discardCandidateCopy.");
            }

            if (decision == RepairPlanDecision.Apply
                && issues.Any(item => !item.AutoFixable))
            {
                decision = RepairPlanDecision.ReportOnly;
                risk = RepairPlanRisk.Blocked;
                safetyNotes.Add(
                    "The deterministic checker marked at least one issue as non-repairable.");
            }

            if (decision == RepairPlanDecision.Apply
                && !visualEvidenceUsed)
            {
                decision = RepairPlanDecision.ReportOnly;
                risk = RepairPlanRisk.Blocked;
                safetyNotes.Add(
                    "Applying an agent or model proposal requires rendered visual evidence.");
            }

            if (decision == RepairPlanDecision.Apply
                && rule.Property == FormatProperty.ParagraphStyleId)
            {
                decision = RepairPlanDecision.Preserve;
                risk = RepairPlanRisk.Blocked;
                safetyNotes.Add(
                    "Paragraph style replacement is protected because it can remove numbering, borders, and inherited layout.");
            }

            if (decision == RepairPlanDecision.Apply
                && rule.Target == RuleTarget.TableText
                && !IsCharacterProperty(rule.Property))
            {
                decision = RepairPlanDecision.Preserve;
                risk = RepairPlanRisk.Blocked;
                safetyNotes.Add(
                    "Table geometry and paragraph structure are protected; only character formatting may change inside table cells.");
            }

            if (decision == RepairPlanDecision.Apply && confidence < 0.80m)
            {
                decision = RepairPlanDecision.ReportOnly;
                risk = RepairPlanRisk.High;
                safetyNotes.Add(
                    "AI confidence below 0.80 requires manual review.");
            }

            bool requiresConfirmation = decision == RepairPlanDecision.Apply
                && (risk != RepairPlanRisk.Low
                    || rule.Target == RuleTarget.Page
                    || IsLayoutAffecting(rule.Property));
            ModificationLevel level = decision != RepairPlanDecision.Apply
                ? ModificationLevel.Advisory
                : requiresConfirmation
                    ? ModificationLevel.Review
                    : ModificationLevel.Safe;
            string directiveId = Id(
                report.ReportId,
                group.GroupId,
                decision.ToString(),
                risk.ToString());
            directives.Add(new RepairPlanDirective(
                directiveId,
                group.GroupId,
                group.Scope,
                group.RuleId,
                decision,
                risk,
                confidence,
                group.IssueIds,
                requiresConfirmation,
                candidate.Reason,
                safetyNotes,
                level,
                candidate.DependsOnGroupIds,
                rollbackStrategy));
        }

        LayoutOperation[] layoutOperations = ValidateLayoutOperations(
            proposedLayoutOperations,
            visualEvidenceUsed);
        string planId = Id(
            report.ReportId,
            sourceSha256,
            providerId,
            model,
            string.Join(
                "|",
                directives.Select(
                    item =>
                        $"{item.ScopeId}:{item.Decision}:{item.Risk}:{item.Confidence}")),
            string.Join(
                "|",
                layoutOperations
                    .OrderBy(
                        item => item.OperationId,
                        StringComparer.Ordinal)
                    .Select(
                        item =>
                            $"{item.OperationId}:{item.Kind}:{item.Decision}:" +
                            $"{item.Risk}:{item.AfterElementId}:" +
                            $"{item.TargetSectionIndex}:{item.ColumnCount}:" +
                            $"{item.ColumnSpacingTwips}:" +
                            $"{string.Join(",", item.DependsOnOperationIds)}")));
        return new RepairPlan(
            planId,
            report.ReportId,
            sourceSha256,
            origin,
            providerId,
            model,
            visualEvidenceUsed,
            externalProcessingConsent,
            directives,
            notices,
            RepairPlan.CurrentSchemaVersion,
            layoutOperations);
    }

    public static string[] ExpandApprovedIssues(
        RepairPlan plan,
        CheckReport report,
        IEnumerable<string> approvedDirectiveIds)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(approvedDirectiveIds);
        if (!string.Equals(
                plan.SourceReportId,
                report.ReportId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The repair plan does not belong to the current check report.",
                nameof(plan));
        }

        HashSet<string> approved = approvedDirectiveIds.ToHashSet(
            StringComparer.Ordinal);
        Dictionary<string, RepairPlanDirective> byId = plan.Directives
            .ToDictionary(item => item.DirectiveId, StringComparer.Ordinal);
        string? unknown = approved.FirstOrDefault(id => !byId.ContainsKey(id));
        if (unknown is not null)
        {
            throw new ArgumentException(
                $"Unknown repair-plan directive '{unknown}'.",
                nameof(approvedDirectiveIds));
        }

        HashSet<string> issueIds = approved
            .Select(id => byId[id])
            .Where(item => item.Decision == RepairPlanDecision.Apply)
            .SelectMany(item => item.IssueIds)
            .ToHashSet(StringComparer.Ordinal);
        return report.Issues
            .Where(item => issueIds.Contains(item.IssueId))
            .Where(item => item.AutoFixable)
            .Select(item => item.IssueId)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsCharacterProperty(FormatProperty property) =>
        property is
            FormatProperty.FontAscii
            or FormatProperty.FontHighAnsi
            or FormatProperty.FontEastAsia
            or FormatProperty.FontComplexScript
            or FormatProperty.FontSize
            or FormatProperty.Bold
            or FormatProperty.Italic;

    private static bool AllowedRollback(string value) =>
        value is
            "discardCandidateCopy"
            or "restoreSectionSnapshot"
            or "preserveOriginalPackage";

    private static LayoutOperation[] ValidateLayoutOperations(
        IEnumerable<ProposedLayoutOperation>? proposed,
        bool visualEvidenceUsed) =>
        (proposed ?? Array.Empty<ProposedLayoutOperation>())
        .Select(
            item =>
            {
                RepairPlanDecision decision = item.Decision;
                RepairPlanRisk risk = item.Risk;
                ModificationLevel level;
                if (item.Kind
                    == LayoutOperationKind.PreserveFullWidthObject)
                {
                    decision = decision == RepairPlanDecision.Apply
                        ? RepairPlanDecision.Preserve
                        : decision;
                    risk = risk < RepairPlanRisk.High
                        ? RepairPlanRisk.High
                        : risk;
                    level = ModificationLevel.Experimental;
                }
                else if (!visualEvidenceUsed
                    || decision != RepairPlanDecision.Apply)
                {
                    decision = RepairPlanDecision.ReportOnly;
                    risk = risk < RepairPlanRisk.High
                        ? RepairPlanRisk.High
                        : risk;
                    level = ModificationLevel.Experimental;
                }
                else
                {
                    risk = risk < RepairPlanRisk.Medium
                        ? RepairPlanRisk.Medium
                        : risk;
                    level = ModificationLevel.Review;
                }

                string rollback = AllowedRollback(item.RollbackStrategy)
                    ? item.RollbackStrategy
                    : "discardCandidateCopy";
                return new LayoutOperation(
                    item.OperationId,
                    item.Kind,
                    decision,
                    risk,
                    level,
                    level == ModificationLevel.Review,
                    item.Reason,
                    rollback,
                    item.DependsOnOperationIds,
                    item.AfterElementId,
                    item.TargetSectionIndex,
                    item.ColumnCount,
                    item.ColumnSpacingTwips,
                    item.ObjectElementId,
                    item.Strategy);
            })
        .ToArray();

    private static bool IsLayoutAffecting(FormatProperty property) =>
        property is
            FormatProperty.PageWidth
            or FormatProperty.PageHeight
            or FormatProperty.PageOrientation
            or FormatProperty.MarginTop
            or FormatProperty.MarginRight
            or FormatProperty.MarginBottom
            or FormatProperty.MarginLeft
            or FormatProperty.ParagraphStyleId;

    private static string ScopeKey(
        FormatRule rule,
        CheckIssue issue)
    {
        StructuralLocation location = issue.DocumentLocation;
        if (rule.Target == RuleTarget.TableText)
        {
            return string.Join(
                ":",
                "table",
                location.Part,
                location.PartIndex,
                location.SectionIndex,
                location.TableIndex);
        }

        if (rule.Property is
                FormatProperty.FirstLineIndent
                or FormatProperty.ParagraphStyleId
            || rule.Target is
                RuleTarget.Title
                or RuleTarget.Heading1
                or RuleTarget.Heading2
                or RuleTarget.Heading3
                or RuleTarget.FigureCaption
                or RuleTarget.TableCaption)
        {
            return "element:" + location;
        }

        return "rule";
    }

    private static string ScopeLabel(
        FormatRule rule,
        CheckIssue issue)
    {
        StructuralLocation location = issue.DocumentLocation;
        if (rule.Target == RuleTarget.TableText)
        {
            int displayIndex = (location.TableIndex ?? 0) + 1;
            return $"Table {displayIndex}";
        }

        if (ScopeKey(rule, issue).StartsWith(
                "element:",
                StringComparison.Ordinal))
        {
            return $"{rule.Target} at {location}";
        }

        return rule.Target.ToString();
    }

    private static string Id(params string[] values)
    {
        byte[] bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join("\u001f", values)));
        return "rp-" + Convert.ToHexString(bytes).ToLowerInvariant()[..24];
    }
}

public sealed record ProposedDirective(
    string GroupId,
    RepairPlanDecision Decision,
    RepairPlanRisk Risk,
    decimal Confidence,
    string Reason,
    IReadOnlyList<string>? DependsOnGroupIds = null,
    string RollbackStrategy = "discardCandidateCopy");

public sealed record RepairPlanCandidateGroup(
    string GroupId,
    string RuleId,
    string Scope,
    IReadOnlyList<string> IssueIds,
    IReadOnlyList<StructuralLocation> Locations);

public sealed record ProposedLayoutOperation(
    string OperationId,
    LayoutOperationKind Kind,
    RepairPlanDecision Decision,
    RepairPlanRisk Risk,
    string Reason,
    IReadOnlyList<string>? DependsOnOperationIds = null,
    string RollbackStrategy = "discardCandidateCopy",
    string? AfterElementId = null,
    int? TargetSectionIndex = null,
    int? ColumnCount = null,
    int? ColumnSpacingTwips = null,
    string? ObjectElementId = null,
    string? Strategy = null);
