using Lockflight.Cli.Gates.Suppressions;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class CSharpMaskTests
{
    // Each source has a literal followed by code: the code after it must survive the mask whole, and no comment opener in the
    // literal may.
    [Theory]
    [InlineData("var s = \"a/*b\"; int after = 1; // */")]
    [InlineData("var s = @\"a\"\"/*\"; int after = 1; // */")]
    [InlineData("var s = $\"{d[\"/*\"]}\"; int after = 1; // */")]
    [InlineData("var s = $@\"{d[\"/*\"]}\"; int after = 1; // */")]
    [InlineData("var s = @$\"{d[\"/*\"]}\"; int after = 1; // */")]
    [InlineData("var s = $@\"{{/*}}\"; int after = 1; // */")]
    [InlineData("var s = $@\"\"\"{x}\"\"/*\"; int after = 1; // */")]
    [InlineData("var s = $\"{(a ? $\"{b[\"/*\"]}\" : c)}\"; int after = 1; // */")]
    [InlineData("var s = $\"{'\"'}/*\"; int after = 1; // */")]
    [InlineData("var s = $\"{'}'}/*\"; int after = 1; // */")]
    [InlineData("var s = $\"{new { A = \"/*\" }.A}\"; int after = 1; // */")]
    [InlineData("var s = \"\"\"a \"\" /* b\"\"\"; int after = 1; // */")]
    [InlineData("var s = $$\"\"\"{{d[\"\"\"/*\"\"\"]}} {not a hole}\"\"\"; int after = 1; // */")]
    [InlineData("var s = $\"{x /* a } */}/*\"; int after = 1; // */")]
    [InlineData("var c = '\"'; int after = 1; // \"")]
    [InlineData("var c = '\\''; int after = 1; // '")]
    public void CodeAfterALiteralSurvivesTheMask(string source)
    {
        string masked = CSharpMask.Mask(source);

        Assert.Equal(source.Length, masked.Length);
        Assert.Contains("int after = 1;", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("/*", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void AHoleOfARegularInterpolatedStringCanSpanLines()
    {
        string source = "var s = $\"{Format(\n    x)} /* \";\nint after = 1; // */\n";

        string masked = CSharpMask.Mask(source);

        Assert.Contains("int after = 1;", masked, StringComparison.Ordinal);
        Assert.Equal(source.Count(character => character == '\n'), masked.Count(character => character == '\n'));
    }

    [Fact]
    public void AnUnterminatedRegularStringEndsAtItsLine()
    {
        string masked = CSharpMask.Mask("var s = \"open\nint after = 1;\n");

        Assert.Contains("int after = 1;", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void LiteralSpansCoverTheWholeStringHolesIncluded()
    {
        string source = "var s = $@\"{d[\"x\"]} y\"; int after = 1;";
        List<(int Start, int End)> literals = [];

        string kept = CSharpMask.MaskComments(source, literals);

        Assert.Equal(source, kept);
        (int start, int end) = Assert.Single(literals);
        Assert.Equal("{d[\"x\"]} y", source[start..end]);
    }

    [Fact]
    public void ASuppressionAfterAVerbatimInterpolatedStringIsStillFound()
    {
        string source = "var s = $@\"{d[\"/*\"]}\";\n#pragma warning disable CA1000\nint after = 1; // */\n";
        string[] raw = source.Split('\n');

        string[] masked = SuppressionPatterns.MaskedCSharpLines(source);

        SuppressionHit hit = Assert.Single(SuppressionPatterns.InLine("A.cs", raw[1], masked[1]));
        Assert.Equal("CA1000", hit.Rule);
    }
}
