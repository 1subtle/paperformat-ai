using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Json.Schema;
using PaperFormat.Checking;
using PaperFormat.Classification;
using PaperFormat.Domain;
using PaperFormat.OpenXml;
using PaperFormat.Reporting;
using PaperFormat.Rules;

namespace PaperFormat.Checking.Tests;

public sealed class CheckReportOutputTests
{
    private static readonly JsonSchema CheckReportSchema = LoadSchema();

    [Theory]
    [InlineData("valid-ieee-like.docx")]
    [InlineData("wrong-format.docx")]
    public void JsonReportConformsToVersionedSchema(string fileName)
    {
        string json = CheckReportJson.Serialize(Check(fileName));
        using JsonDocument instance = JsonDocument.Parse(json);

        EvaluationResults results = CheckReportSchema.Evaluate(
            instance.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
            });

        Assert.True(results.IsValid, Diagnostics(results));
    }

    [Fact]
    public void JsonReportIsDeterministicAndDoesNotContainManuscriptText()
    {
        string first = CheckReportJson.Serialize(Check("wrong-format.docx"));
        string second = CheckReportJson.Serialize(Check("wrong-format.docx"));

        Assert.Equal(first, second);
        Assert.DoesNotContain(
            "Each package uses fixed metadata",
            first,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "synthetic manuscript exercises",
            first,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HtmlReportIsStandaloneAndEscapesEveryDynamicValue()
    {
        var report = new CheckReport(
            "report<script>",
            "rules<script>",
            1,
            CheckStatus.IssuesFound,
            new CheckSummary(1, 1, 0, 1, 0, 1, 0, 0, 0, 0),
            [
                new CheckIssue(
                    "issue<script>",
                    "rule<script>",
                    RuleSeverity.Warning,
                    RuleTarget.Body,
                    new StructuralLocation(
                        DocumentPartKind.MainDocument,
                        sectionIndex: 0,
                        paragraphIndex: 0),
                    new TextRuleValue("<script>alert(1)</script>"),
                    new TextRuleValue("safe"),
                    "<script>alert(2)</script>",
                    new RuleEvidence(
                        RuleEvidenceKind.UserOverride,
                        "provider<script>",
                        "reference<script>"),
                    1m,
                    true),
            ],
            Array.Empty<SkippedRule>(),
            Array.Empty<PendingElement>());

        string html = CheckReportHtml.Render(report);

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("<style>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WrongReportMatchesApprovedCanonicalSnapshot()
    {
        CheckReport report = Check("wrong-format.docx");
        string json = CheckReportJson.Serialize(report);
        using JsonDocument snapshot = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Snapshots",
                    "wrong-format-report-v1.json")));
        JsonElement root = snapshot.RootElement;
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();

        Assert.Equal(
            report.Summary.IssueCount,
            root.GetProperty("issueCount").GetInt32());
        Assert.Equal(
            report.Summary.Score,
            root.GetProperty("score").GetInt32());
        string? expectedDigest =
            root.GetProperty("canonicalJsonSha256").GetString();
        Assert.True(
            string.Equals(expectedDigest, digest, StringComparison.Ordinal),
            $"Expected digest: {expectedDigest}; actual digest: {digest}");
    }

    private static CheckReport Check(string fileName)
    {
        DocumentParseResult parsed = WordDocumentParser.Parse(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
        DocumentModel document = Assert.IsType<DocumentModel>(parsed.Document);
        ClassificationSet classifications =
            new DeterministicDocumentClassifier().Classify(document);
        RulePackage rules = new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));
        return new FormatCheckEngine().Check(
            document,
            rules,
            classifications);
    }

    private static JsonSchema LoadSchema() =>
        JsonSchema.FromText(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Schemas",
                    "check-report.schema.json")));

    private static string Diagnostics(EvaluationResults results) =>
        string.Join(
            Environment.NewLine,
            Flatten(results)
                .Where(result => !result.IsValid && result.Errors is not null)
                .Take(40)
                .Select(
                    result =>
                        $"{result.InstanceLocation}: " +
                        string.Join(
                            "; ",
                            result.Errors!.Select(
                                error => $"{error.Key}={error.Value}"))));

    private static IEnumerable<EvaluationResults> Flatten(
        EvaluationResults result)
    {
        yield return result;
        if (result.Details is null)
        {
            yield break;
        }

        foreach (EvaluationResults detail in result.Details)
        {
            foreach (EvaluationResults nested in Flatten(detail))
            {
                yield return nested;
            }
        }
    }
}
