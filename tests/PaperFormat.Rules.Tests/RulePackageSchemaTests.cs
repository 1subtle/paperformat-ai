using System.Text.Json;
using Json.Schema;
using PaperFormat.Domain;
using PaperFormat.OpenXml;
using PaperFormat.Rules;

namespace PaperFormat.Rules.Tests;

public sealed class RulePackageSchemaTests
{
    private static readonly JsonSchema RulePackageSchema = LoadSchema();
    [Fact]
    public void BuiltInPackageConformsToVersionedSchema()
    {
        RulePackage package = new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));

        AssertSchemaValid(RulePackageJson.Serialize(package));
    }

    [Fact]
    public void ExtractedPackageConformsToVersionedSchema()
    {
        DocumentParseResult parsed = WordDocumentParser.Parse(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "valid-ieee-like.docx"));
        DocumentModel document = Assert.IsType<DocumentModel>(parsed.Document);
        RulePackage package = new WordTemplateRuleProvider().Extract(
            new WordTemplateFormatRequirementSource(
                "valid-ieee-like.docx",
                document));

        AssertSchemaValid(RulePackageJson.Serialize(package));
    }

    [Fact]
    public void SchemaRejectsUnknownProperties()
    {
        const string invalid =
            """
            {
              "schemaVersion": "1.0",
              "packageId": "x",
              "revision": 1,
              "name": "x",
              "providerId": "x",
              "providerVersion": "1",
              "sourceReference": "x",
              "rules": [],
              "notices": [],
              "unexpected": true
            }
            """;

        EvaluationResults results = Evaluate(invalid);

        Assert.False(results.IsValid);
    }

    private static void AssertSchemaValid(string json)
    {
        EvaluationResults results = Evaluate(json);
        Assert.True(
            results.IsValid,
            Diagnostics(results));
    }

    private static EvaluationResults Evaluate(string json)
    {
        using JsonDocument instance = JsonDocument.Parse(json);
        return RulePackageSchema.Evaluate(
            instance.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
            });
    }

    private static JsonSchema LoadSchema() =>
        JsonSchema.FromText(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Schemas",
                    "rule-package.schema.json")));

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
