using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Json.Schema;
using PaperFormat.Ai;
using PaperFormat.Checking;
using PaperFormat.Classification;
using PaperFormat.Domain;
using PaperFormat.OpenXml;
using PaperFormat.Repair;
using PaperFormat.Reporting;
using PaperFormat.Rules;

namespace PaperFormat.Repair.Tests;

public sealed class RepairReportSchemaTests
{
    private static readonly ConcurrentDictionary<string, JsonSchema>
        SchemaCache = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions ArtifactJsonOptions =
        CreateArtifactJsonOptions();

    [Fact]
    public void RealChangeLogAndIntegrityReportConformToSchemas()
    {
        string source = Fixture("wrong-format.docx");
        RulePackage rules = Rules();
        CheckReport preCheck = Check(source, rules);
        string[] selected = preCheck.Issues
            .Where(
                issue => issue.AutoFixable
                    && issue.ElementType != RuleTarget.Page)
            .Select(issue => issue.IssueId)
            .ToArray();
        RepairResult result = SafeRepairService.Execute(
            source,
            OutputPath(),
            rules,
            preCheck,
            new RepairSelection(selected));

        AssertSchema(
            "change-log.schema.json",
            ChangeLogJson.Serialize(result.ChangeLog));
        AssertSchema(
            "integrity-report.schema.json",
            IntegrityReportJson.Serialize(result.Integrity));
    }

    [Fact]
    public void RepairReportsAreContentSafe()
    {
        string source = Fixture("wrong-format.docx");
        RulePackage rules = Rules();
        CheckReport preCheck = Check(source, rules);
        CheckIssue issue = preCheck.Issues.First(
            item => item.AutoFixable
                && item.ElementType != RuleTarget.Page);
        RepairResult result = SafeRepairService.Execute(
            source,
            OutputPath(),
            rules,
            preCheck,
            new RepairSelection([issue.IssueId]));

        string combined =
            ChangeLogJson.Serialize(result.ChangeLog)
            + IntegrityReportJson.Serialize(result.Integrity);
        Assert.DoesNotContain(
            "Each package uses fixed metadata",
            combined,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "synthetic manuscript exercises",
            combined,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HybridReviewContractsConformToSchemas()
    {
        string source = Fixture("wrong-format.docx");
        RulePackage rules = Rules();
        CheckReport report = Check(source, rules);
        FormatRule indent = Assert.Single(
            rules.Rules,
            rule => rule.Target == RuleTarget.Body
                && rule.Property == FormatProperty.FirstLineIndent);
        RepairPlanCandidateGroup group =
            RepairPlanPolicy.CreateCandidateGroups(report, rules)
                .First(item => item.RuleId == indent.RuleId);
        RepairPlan plan = RepairPlanPolicy.Validate(
            report,
            rules,
            [
                new ProposedDirective(
                    group.GroupId,
                    RepairPlanDecision.Apply,
                    RepairPlanRisk.Medium,
                    0.98m,
                    "Normal body paragraphs need the confirmed indentation."),
            ],
            "test-provider",
            "test-model",
            RepairPlanOrigin.OpenAi,
            visualEvidenceUsed: true,
            externalProcessingConsent: true,
            sourceSha256: Sha256(source));
        var visual = new VisualReviewReport(
            VisualReviewStatus.Passed,
            "test-provider",
            "test-model",
            3,
            3,
            [
                new VisualReviewFinding(
                    "layout.stable",
                    RepairPlanRisk.Low,
                    "No material visual regression was found.",
                    1,
                    1),
            ],
            "Rendered pages remain stable.");
        AssertSchema(
            "repair-plan.schema.json",
            RepairPlanJson.Serialize(plan));
        AssertSchema(
            "visual-review.schema.json",
            JsonSerializer.Serialize(visual, ArtifactJsonOptions));
    }

    [Fact]
    public void ExternalAgentSubmissionContractsConformToSchemas()
    {
        var proposal = new
        {
            schemaVersion = "2.0",
            sourceReportId = "report-1",
            sourceSha256 =
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            providerId = "codex",
            model = "user-selected",
            visualEvidenceUsed = true,
            externalProcessingConsent = true,
            directives = new[]
            {
                new
                {
                    groupId = "group-1",
                    decision = "apply",
                    risk = "medium",
                    confidence = 0.98m,
                    reason = "Rendered body paragraphs lack the required indent.",
                },
                new
                {
                    groupId = "table-algorithm-1",
                    decision = "preserve",
                    risk = "high",
                    confidence = 0.99m,
                    reason = "Preserve the algorithm table geometry.",
                },
            },
            layoutOperations = Array.Empty<object>(),
        };
        var review = new
        {
            schemaVersion = "1.0",
            planId = "plan-1",
            operationId = "operation-1",
            status = "passed",
            providerId = "codex",
            model = "user-selected",
            sourcePageCount = 8,
            outputPageCount = 8,
            findings = new[]
            {
                new
                {
                    code = "layout.stable",
                    risk = "low",
                    message = "Title, algorithm table, and paragraph indents remain correct.",
                    sourcePage = 1,
                    outputPage = 1,
                },
            },
            summary = "The deterministic repair introduced no visual regression.",
        };

        AssertSchema(
            "agent-plan-proposal.schema.json",
            JsonSerializer.Serialize(proposal));
        AssertSchema(
            "agent-visual-review-submission.schema.json",
            JsonSerializer.Serialize(review));
    }

    private static void AssertSchema(string schemaName, string json)
    {
        JsonSchema schema = SchemaCache.GetOrAdd(
            schemaName,
            name => JsonSchema.FromText(
                File.ReadAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Schemas",
                        name))));
        using JsonDocument instance = JsonDocument.Parse(json);
        EvaluationResults result = schema.Evaluate(
            instance.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
            });
        Assert.True(result.IsValid, Diagnostics(result));
    }

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

    private static CheckReport Check(string path, RulePackage rules)
    {
        DocumentParseResult parsed = WordDocumentParser.Parse(path);
        DocumentModel document = Assert.IsType<DocumentModel>(parsed.Document);
        ClassificationSet classifications =
            new DeterministicDocumentClassifier().Classify(document);
        return new FormatCheckEngine().Check(
            document,
            rules,
            classifications);
    }

    private static RulePackage Rules() =>
        new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));

    private static string Fixture(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
    }

    private static string OutputPath()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "paperformat-schema-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "repaired.docx");
    }

    private static JsonSerializerOptions CreateArtifactJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
