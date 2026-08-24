namespace PaperFormat.Domain.Tests;

public sealed class TwipTests
{
    [Fact]
    public void ConversionsUseCanonicalTwipUnit()
    {
        Assert.Equal(new Twip(200), Twip.FromPoints(10m));
        Assert.Equal(new Twip(1440), Twip.FromInches(1m));
        Assert.Equal(new Twip(1440), Twip.FromCentimeters(2.54m));
        Assert.Equal(new Twip(1440), Twip.FromMillimeters(25.4m));
        Assert.Equal(72m, Twip.FromInches(1m).Points);
        Assert.Equal(1m, Twip.FromPoints(72m).Inches);
        Assert.True(Twip.FromPoints(9m) < Twip.FromPoints(10m));
        Assert.True(Twip.FromPoints(10m) >= Twip.FromPoints(10m));
    }

    [Fact]
    public void ConversionRoundingIsSymmetricAndDeterministic()
    {
        Assert.Equal(new Twip(1), Twip.FromPoints(0.025m));
        Assert.Equal(new Twip(-1), Twip.FromPoints(-0.025m));
        Assert.Equal(
            Twip.FromPoints(10.0m),
            Twip.FromPoints(10.0000m));
    }

    [Fact]
    public void LineMultipleNormalizesFactorToWordUnits()
    {
        var multiple = LineMultiple.FromFactor(1.5m);

        Assert.Equal(360, multiple.Value);
        Assert.Equal(1.5m, multiple.Factor);
        Assert.Equal(
            LineSpacing.Automatic(new LineMultiple(360)),
            LineSpacing.Automatic(multiple));
    }

    [Fact]
    public void LineSpacingRejectsMismatchedRepresentations()
    {
        Assert.Throws<ArgumentException>(
            () => new LineSpacing(LineSpacingKind.Auto));
        Assert.Throws<ArgumentException>(
            () => new LineSpacing(
                LineSpacingKind.Exact,
                multiple: new LineMultiple(240)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LineSpacing.Exact(new Twip(0)));
    }
}
