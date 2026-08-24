using System.Text.Json;

namespace PaperFormat.Domain.Tests;

public sealed class StructuralLocationTests
{
    [Fact]
    public void CanonicalPathUsesStableStructuralIndices()
    {
        var location = new StructuralLocation(
            DocumentPartKind.MainDocument,
            sectionIndex: 2,
            paragraphIndex: 4,
            tableIndex: 1,
            rowIndex: 0,
            cellIndex: 3,
            runIndex: 5,
            relationshipId: "rId7");

        Assert.Equal(
            "main/section[2]/table[1]/row[0]/cell[3]/paragraph[4]/run[5]/relationship[rId7]",
            location.CanonicalPath);
        Assert.Equal(location.CanonicalPath, location.ToString());
    }

    [Fact]
    public void HeaderPartIndexIsIncludedWithoutRenderedPageNumber()
    {
        var location = new StructuralLocation(
            DocumentPartKind.Header,
            partIndex: 1,
            sectionIndex: 0,
            paragraphIndex: 3);

        Assert.Equal(
            "header[1]/section[0]/paragraph[3]",
            location.CanonicalPath);
    }

    [Fact]
    public void OrderingUsesNumericIndicesInsteadOfLexicalPaths()
    {
        var paragraphTwo = new StructuralLocation(
            DocumentPartKind.MainDocument,
            sectionIndex: 0,
            paragraphIndex: 2);
        var paragraphTen = new StructuralLocation(
            DocumentPartKind.MainDocument,
            sectionIndex: 0,
            paragraphIndex: 10);

        Assert.True(paragraphTwo.CompareTo(paragraphTen) < 0);
        Assert.True(paragraphTwo < paragraphTen);
        Assert.True(paragraphTen >= paragraphTwo);
        Assert.Equal(
            new[] { paragraphTwo, paragraphTen },
            new[] { paragraphTen, paragraphTwo }.Order().ToArray());
    }

    [Fact]
    public void EqualLocationsSerializeIdentically()
    {
        var first = new StructuralLocation(
            DocumentPartKind.MainDocument,
            sectionIndex: 0,
            paragraphIndex: 12,
            runIndex: 2);
        var second = new StructuralLocation(
            DocumentPartKind.MainDocument,
            sectionIndex: 0,
            paragraphIndex: 12,
            runIndex: 2);

        Assert.Equal(first, second);
        Assert.Equal(
            JsonSerializer.Serialize(first),
            JsonSerializer.Serialize(second));
    }

    [Fact]
    public void InvalidHierarchyIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StructuralLocation(
                DocumentPartKind.MainDocument,
                sectionIndex: -1));
        Assert.Throws<ArgumentException>(
            () => new StructuralLocation(
                DocumentPartKind.MainDocument,
                rowIndex: 0));
        Assert.Throws<ArgumentException>(
            () => new StructuralLocation(
                DocumentPartKind.MainDocument,
                runIndex: 0));
    }
}
