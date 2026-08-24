using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PaperFormat.Domain;
using PaperFormat.Rules;

namespace PaperFormat.Rules.Tests;

public sealed class BuiltInIeeeRuleProviderTests
{
    [Fact]
    public void ProviderRejectsUnknownSources()
    {
        var provider = new BuiltInIeeeRuleProvider();
        var unknown = new BuiltInFormatRequirementSource("unknown");

        Assert.False(provider.CanHandle(unknown));
        Assert.Throws<ArgumentException>(() => provider.Extract(unknown));
    }

    [Fact]
    public void ProfileCoversEveryMvpTargetAndRequiredPageProperty()
    {
        RulePackage package = Extract();

        Assert.Equal(
            Enum.GetValues<RuleTarget>(),
            package.Rules
                .Select(rule => rule.Target)
                .Distinct()
                .Order()
                .ToArray());
        Assert.Equal(
            Enum.GetValues<FormatProperty>(),
            package.Rules
                .Select(rule => rule.Property)
                .Distinct()
                .Order()
                .ToArray());
        AssertPageRule(package, FormatProperty.PageWidth, new Twip(12_240));
        AssertPageRule(package, FormatProperty.PageHeight, new Twip(15_840));
        AssertPageRule(package, FormatProperty.MarginLeft, new Twip(720));
        AssertPageRule(package, FormatProperty.ColumnSpacing, new Twip(360));
        FormatRule columns = Rule(
            package,
            RuleTarget.Page,
            FormatProperty.ColumnCount);
        Assert.Equal(2, Assert.IsType<IntegerRuleValue>(columns.Expected).Value);
    }

    [Fact]
    public void ProfileContainsCheckAndRepairPolicyRules()
    {
        RulePackage package = Extract();

        Assert.Equal(
            RepairLevel.RequiresConfirmation,
            Rule(package, RuleTarget.Page, FormatProperty.PageWidth).RepairLevel);
        Assert.Equal(
            RepairLevel.Safe,
            Rule(package, RuleTarget.Body, FormatProperty.FontSize).RepairLevel);
        Assert.Equal(
            RepairLevel.None,
            Rule(
                package,
                RuleTarget.FigureCaption,
                FormatProperty.CaptionNumberSequence).RepairLevel);
        Assert.All(
            package.Rules,
            rule =>
            {
                Assert.Equal(1m, rule.Confidence);
                Assert.Equal(RuleEvidenceKind.BuiltIn, rule.Evidence.Kind);
                Assert.True(rule.Enabled);
                Assert.False(rule.NeedsConfirmation);
            });
    }

    [Fact]
    public void ProfileSerializationIsDeterministic()
    {
        string first = RulePackageJson.Serialize(Extract());
        string second = RulePackageJson.Serialize(Extract());

        Assert.Equal(first, second);
    }

    [Fact]
    public void ProfileMatchesApprovedCanonicalSnapshot()
    {
        RulePackage package = Extract();
        string json = RulePackageJson.Serialize(package);
        using JsonDocument snapshot = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Snapshots",
                    "builtin-ieee-v1.json")));
        JsonElement root = snapshot.RootElement;
        string digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();

        Assert.Equal(
            BuiltInIeeeRuleProvider.ProfileId,
            root.GetProperty("profileId").GetString());
        Assert.Equal(
            package.SchemaVersion,
            root.GetProperty("schemaVersion").GetString());
        Assert.Equal(
            package.Rules.Count,
            root.GetProperty("ruleCount").GetInt32());
        string? expectedDigest =
            root.GetProperty("canonicalJsonSha256").GetString();
        Assert.True(
            string.Equals(expectedDigest, digest, StringComparison.Ordinal),
            $"Expected digest: {expectedDigest}; actual digest: {digest}");
    }

    private static RulePackage Extract() =>
        new BuiltInIeeeRuleProvider().Extract(
            new BuiltInFormatRequirementSource(
                BuiltInIeeeRuleProvider.ProfileId));

    private static void AssertPageRule(
        RulePackage package,
        FormatProperty property,
        Twip expected)
    {
        FormatRule rule = Rule(package, RuleTarget.Page, property);
        Assert.Equal(expected, Assert.IsType<TwipRuleValue>(rule.Expected).Value);
    }

    private static FormatRule Rule(
        RulePackage package,
        RuleTarget target,
        FormatProperty property) =>
        Assert.Single(
            package.Rules,
            rule => rule.Target == target && rule.Property == property);
}
