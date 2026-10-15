using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Tests.Gates;

public sealed class ChangedLinesTests
{
    [Fact]
    public void HunksYieldTheAddedLinesPerFile()
    {
        const string diff = """
            diff --git a/src/A.cs b/src/A.cs
            --- a/src/A.cs
            +++ b/src/A.cs
            @@ -3,0 +4,2 @@ class A
            +    int x;
            +    int y;
            @@ -20 +22 @@
            -old
            +new
            diff --git a/src/Gone.cs b/src/Gone.cs
            --- a/src/Gone.cs
            +++ /dev/null
            @@ -1,3 +0,0 @@
            """;
        Dictionary<string, HashSet<int>> lines = [];

        ChangedLines.AddFromDiff(lines, diff);

        Assert.Equal([4, 5, 22], lines["src/A.cs"].Order());
        Assert.False(lines.ContainsKey("src/Gone.cs"));
    }

    [Fact]
    public void PureDeletionHunksAddNoLines()
    {
        Dictionary<string, HashSet<int>> lines = [];

        ChangedLines.AddFromDiff(lines, "+++ b/src/A.cs\n@@ -5,2 +4,0 @@\n");

        Assert.Empty(lines["src/A.cs"]);
    }

    [Theory]
    [InlineData("tests/Shop.Tests/ShippingTests.cs", true)]
    [InlineData("src/Shop.UnitTests/A.cs", true)]
    [InlineData("test/A.cs", true)]
    [InlineData("src/Shop.Api/PriceCalculator.cs", false)]
    [InlineData("src/Contest/Entry.cs", false)]
    public void TestCodeIsRecognisedByFolderConvention(string file, bool expected) =>
        Assert.Equal(expected, Lockflight.Cli.Gates.Steps.CoverageStep.IsTestCode(file));
}
