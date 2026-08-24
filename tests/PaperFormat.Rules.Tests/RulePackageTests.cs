using PaperFormat.Domain;
using PaperFormat.Rules;

namespace PaperFormat.Rules.Tests;

public sealed class RulePackageTests
{
    [Fact]
    public void FormatRuleRejectsMismatchedValueType()
    {
        Assert.Throws<ArgumentException>(
            () => new FormatRule(
                "rule.font-size",
                RuleTarget.Body,
                FormatProperty.FontSize,
                new TextRuleValue("10 pt"),
                RuleSeverity.Warning,
                RepairLevel.Safe,
                Evidence(),
                confidence: 1m));
    }

    [Fact]
    public void RulePackageSortsRulesAndRejectsDuplicateIdentifiers()
    {
        FormatRule second = Rule(
            "rule.z",
            FormatProperty.Bold,
            new BooleanRuleValue(false));
        FormatRule first = Rule(
            "rule.a",
            FormatProperty.FontSize,
            new TwipRuleValue(Twip.FromPoints(10m)));

        RulePackage package = Package([second, first]);

        Assert.Equal(["rule.a", "rule.z"], package.Rules.Select(rule => rule.RuleId));
        Assert.Throws<ArgumentException>(() => Package([first, first]));
    }

    [Fact]
    public void CanonicalJsonRoundTripsPolymorphicValues()
    {
        RulePackage package = new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));

        string first = RulePackageJson.Serialize(package);
        RulePackage restored = RulePackageJson.Deserialize(first);
        string second = RulePackageJson.Serialize(restored);

        Assert.Equal(first, second);
        Assert.Equal(package.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(package.PackageId, restored.PackageId);
        Assert.Equal(package.Revision, restored.Revision);
        Assert.Equal(package.Name, restored.Name);
        Assert.Equal(package.ProviderId, restored.ProviderId);
        Assert.Equal(package.ProviderVersion, restored.ProviderVersion);
        Assert.Equal(package.SourceReference, restored.SourceReference);
        Assert.Equal(package.Rules, restored.Rules);
        Assert.Equal(package.Notices, restored.Notices);
        Assert.Contains("\"kind\": \"twip\"", first, StringComparison.Ordinal);
        Assert.Contains(
            "\"kind\": \"lineSpacing\"",
            first,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EditorCreatesANewUserSourcedRevision()
    {
        RulePackage original = Package(
        [
            Rule(
                "rule.font-size",
                FormatProperty.FontSize,
                new TwipRuleValue(Twip.FromPoints(10m))),
        ]);

        RulePackage edited = RulePackageEditor.Apply(
            original,
        [
            new RuleOverride(
                "rule.font-size",
                new TwipRuleValue(Twip.FromPoints(11m)),
                enabled: false),
        ]);

        FormatRule rule = Assert.Single(edited.Rules);
        Assert.Equal(1, original.Revision);
        Assert.Equal(2, edited.Revision);
        Assert.Equal(
            Twip.FromPoints(11m),
            Assert.IsType<TwipRuleValue>(rule.Expected).Value);
        Assert.False(rule.Enabled);
        Assert.False(rule.NeedsConfirmation);
        Assert.Equal(RuleEvidenceKind.UserOverride, rule.Evidence.Kind);
        Assert.Equal(
            Twip.FromPoints(10m),
            Assert.IsType<TwipRuleValue>(
                original.Rules[0].Expected).Value);
    }

    [Fact]
    public void EditorRejectsUnknownAndDuplicateOverrides()
    {
        RulePackage package = Package(
        [
            Rule(
                "rule.bold",
                FormatProperty.Bold,
                new BooleanRuleValue(false)),
        ]);

        Assert.Throws<ArgumentException>(
            () => RulePackageEditor.Apply(
                package,
                [new RuleOverride("missing", enabled: false)]));
        Assert.Throws<ArgumentException>(
            () => RulePackageEditor.Apply(
                package,
                [
                    new RuleOverride("rule.bold", enabled: false),
                    new RuleOverride("rule.bold", enabled: true),
                ]));
    }

    private static RulePackage Package(IEnumerable<FormatRule> rules) =>
        new(
            "test-package",
            revision: 1,
            "Test package",
            "test",
            "1.0.0",
            "test:source",
            rules);

    private static FormatRule Rule(
        string ruleId,
        FormatProperty property,
        RuleValue value) =>
        new(
            ruleId,
            RuleTarget.Body,
            property,
            value,
            RuleSeverity.Warning,
            RepairLevel.Safe,
            Evidence(),
            confidence: 1m);

    private static RuleEvidence Evidence() =>
        new(RuleEvidenceKind.BuiltIn, "test", "test:source");
}
