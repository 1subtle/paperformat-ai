using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using PaperFormat.Domain;

namespace PaperFormat.Reporting;

/// <summary>
/// Produces a standalone, escaped HTML view of a content-safe check report.
/// </summary>
public static class CheckReportHtml
{
    public static string Render(CheckReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var html = new StringBuilder();
        html.Append(
            """
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>PaperFormat check report</title>
              <style>
                :root { color-scheme: light; font-family: system-ui, sans-serif; }
                body { margin: 2rem auto; max-width: 1100px; padding: 0 1rem; color: #17202a; }
                h1, h2 { line-height: 1.2; }
                .summary { display: grid; grid-template-columns: repeat(auto-fit,minmax(9rem,1fr)); gap: .75rem; }
                .card { border: 1px solid #d5d8dc; border-radius: .5rem; padding: .8rem; }
                .label { color: #566573; font-size: .8rem; text-transform: uppercase; }
                .value { font-size: 1.4rem; font-weight: 650; }
                table { border-collapse: collapse; width: 100%; font-size: .9rem; }
                th, td { border: 1px solid #d5d8dc; padding: .55rem; text-align: left; vertical-align: top; }
                th { background: #f4f6f7; }
                code { overflow-wrap: anywhere; }
                .empty { color: #566573; }
              </style>
            </head>
            <body>
            """);
        html.Append("<h1>PaperFormat check report</h1>");
        html.Append("<p>Report <code>");
        Encode(html, report.ReportId);
        html.Append("</code> · Rule package <code>");
        Encode(html, report.RulePackageId);
        html.Append("</code> revision ");
        html.Append(
            report.RulePackageRevision.ToString(CultureInfo.InvariantCulture));
        html.Append("</p><section class=\"summary\">");
        SummaryCard(html, "Status", report.Status.ToString());
        SummaryCard(
            html,
            "Score",
            report.Summary.Score.ToString(CultureInfo.InvariantCulture));
        SummaryCard(
            html,
            "Issues",
            report.Summary.IssueCount.ToString(CultureInfo.InvariantCulture));
        SummaryCard(
            html,
            "Pending",
            report.Summary.PendingElementCount.ToString(
                CultureInfo.InvariantCulture));
        SummaryCard(
            html,
            "Evaluated",
            report.Summary.EvaluatedObservations.ToString(
                CultureInfo.InvariantCulture));
        SummaryCard(
            html,
            "Skipped",
            report.Summary.SkippedRuleCount.ToString(
                CultureInfo.InvariantCulture));
        html.Append("</section>");

        RenderIssues(html, report.Issues);
        RenderPending(html, report.PendingElements);
        RenderSkipped(html, report.SkippedRules);
        html.Append("</body></html>");
        return html.ToString();
    }

    private static void RenderIssues(
        StringBuilder html,
        IReadOnlyCollection<CheckIssue> issues)
    {
        html.Append("<h2>Issues</h2>");
        if (issues.Count == 0)
        {
            html.Append("<p class=\"empty\">No format issues were found.</p>");
            return;
        }

        html.Append(
            "<table><thead><tr><th>Severity</th><th>Rule</th>" +
            "<th>Element</th><th>Location</th><th>Current</th>" +
            "<th>Expected</th><th>Message</th><th>Source</th>" +
            "<th>Auto-fixable</th></tr></thead><tbody>");
        foreach (CheckIssue issue in issues)
        {
            html.Append("<tr>");
            Cell(html, issue.Severity.ToString());
            Cell(html, issue.RuleId, code: true);
            Cell(html, issue.ElementType.ToString());
            Cell(html, issue.DocumentLocation.CanonicalPath, code: true);
            Cell(html, Display(issue.CurrentValue));
            Cell(html, Display(issue.ExpectedValue));
            Cell(html, issue.Message);
            Cell(
                html,
                $"{issue.RuleSource.Kind}: {issue.RuleSource.ProviderId} / " +
                issue.RuleSource.Reference);
            Cell(html, issue.AutoFixable ? "Yes" : "No");
            html.Append("</tr>");
        }

        html.Append("</tbody></table>");
    }

    private static void RenderPending(
        StringBuilder html,
        IReadOnlyCollection<PendingElement> pending)
    {
        html.Append("<h2>Elements requiring confirmation</h2>");
        if (pending.Count == 0)
        {
            html.Append("<p class=\"empty\">No classifications require confirmation.</p>");
            return;
        }

        html.Append(
            "<table><thead><tr><th>Element ID</th><th>Location</th>" +
            "<th>Proposed type</th><th>Confidence</th></tr></thead><tbody>");
        foreach (PendingElement item in pending)
        {
            html.Append("<tr>");
            Cell(html, item.ElementId, code: true);
            Cell(html, item.Location.CanonicalPath, code: true);
            Cell(html, item.ProposedKind.ToString());
            Cell(
                html,
                item.Confidence.ToString("0.00", CultureInfo.InvariantCulture));
            html.Append("</tr>");
        }

        html.Append("</tbody></table>");
    }

    private static void RenderSkipped(
        StringBuilder html,
        IReadOnlyCollection<SkippedRule> skipped)
    {
        html.Append("<h2>Skipped rules</h2>");
        if (skipped.Count == 0)
        {
            html.Append("<p class=\"empty\">No rules were skipped.</p>");
            return;
        }

        html.Append(
            "<table><thead><tr><th>Rule</th><th>Reason</th>" +
            "<th>Message</th></tr></thead><tbody>");
        foreach (SkippedRule item in skipped)
        {
            html.Append("<tr>");
            Cell(html, item.RuleId, code: true);
            Cell(html, item.ReasonCode, code: true);
            Cell(html, item.Message);
            html.Append("</tr>");
        }

        html.Append("</tbody></table>");
    }

    private static void SummaryCard(
        StringBuilder html,
        string label,
        string value)
    {
        html.Append("<div class=\"card\"><div class=\"label\">");
        Encode(html, label);
        html.Append("</div><div class=\"value\">");
        Encode(html, value);
        html.Append("</div></div>");
    }

    private static void Cell(
        StringBuilder html,
        string value,
        bool code = false)
    {
        html.Append("<td>");
        if (code)
        {
            html.Append("<code>");
        }

        Encode(html, value);
        if (code)
        {
            html.Append("</code>");
        }

        html.Append("</td>");
    }

    private static void Encode(StringBuilder html, string value) =>
        html.Append(HtmlEncoder.Default.Encode(value));

    private static string Display(RuleValue? value) =>
        value switch
        {
            null => "Unknown",
            TwipRuleValue item =>
                $"{item.Value.Value.ToString(CultureInfo.InvariantCulture)} twip",
            IntegerRuleValue item =>
                item.Value.ToString(CultureInfo.InvariantCulture),
            BooleanRuleValue item => item.Value ? "true" : "false",
            TextRuleValue item => item.Value,
            PageOrientationRuleValue item => item.Value.ToString(),
            ParagraphAlignmentRuleValue item => item.Value.ToString(),
            LineSpacingRuleValue item => Display(item.Value),
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unsupported rule value."),
        };

    private static string Display(LineSpacing spacing) =>
        spacing.Kind == LineSpacingKind.Auto
            ? "automatic × " +
              spacing.Multiple!.Value.Value.ToString(
                  CultureInfo.InvariantCulture)
            : spacing.Kind + " " +
              spacing.Length!.Value.Value.ToString(
                  CultureInfo.InvariantCulture) +
              " twip";
}
