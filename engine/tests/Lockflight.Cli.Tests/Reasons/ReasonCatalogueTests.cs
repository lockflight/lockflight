using System.Text.RegularExpressions;
using Lockflight.Cli.Evidence.Blocked;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Reasons;
using Lockflight.Cli.Tests.Schemas;

namespace Lockflight.Cli.Tests.Reasons;

public sealed partial class ReasonCatalogueTests
{
    private static readonly string GuidePath = Path.Combine(SchemaFiles.RepositoryRoot, "docs", "guides", "reasons.md");

    private static readonly HashSet<string> GuideAnchors = [.. File.ReadLines(GuidePath).Select(line => Heading().Match(line)).Where(match => match.Success).Select(match => Slug(match.Groups["text"].Value))];

    [Fact]
    public void EveryReasonCodeHasANextLineAndAnEntryInTheReasonsGuide()
    {
        Assert.All(ReasonCodeNames.Named, code =>
        {
            Assert.False(string.IsNullOrWhiteSpace(ReasonCatalogue.Next(code)), $"{code} has no Next line");
            Assert.True(GuideAnchors.Contains(ReasonCatalogue.Anchor(code)!), $"docs/guides/reasons.md has no heading for {ReasonCodeNames.ToName(code)}");
            Assert.Equal($"{ReasonCatalogue.GuideUrl}#{ReasonCatalogue.Anchor(code)}", ReasonCatalogue.Link(code));
        });
    }

    [Fact]
    public void EveryBlockedReasonHasANextLineAndAnEntryInTheReasonsGuide()
    {
        Assert.All(Enum.GetValues<BlockedReasonCode>().Where(code => code != BlockedReasonCode.Unknown), code =>
        {
            Assert.False(string.IsNullOrWhiteSpace(BlockedNextSteps.Next(code)), $"{code} has no Next line");
            Assert.True(GuideAnchors.Contains(BlockedNextSteps.Anchor(code)!), $"docs/guides/reasons.md has no heading for {BlockedNames.ToName(code)}");
        });
    }

    [Fact]
    public void EveryCodeHeadingInTheGuideNamesACodeThisEngineKnows()
    {
        HashSet<string> known = [.. ReasonCodeNames.Named.Select(code => ReasonCatalogue.Anchor(code)!), .. Enum.GetValues<BlockedReasonCode>().Select(BlockedNextSteps.Anchor).OfType<string>()];
        string[] codeHeadings = [.. File.ReadLines(GuidePath).Where(line => line.StartsWith("### ", StringComparison.Ordinal)).Select(line => Slug(line[4..]))];

        Assert.All(codeHeadings, anchor => Assert.Contains(anchor, known));
    }

    [Fact]
    public void NoneAndUnknownHaveNoEntry()
    {
        Assert.Equal(string.Empty, ReasonCatalogue.Next(ReasonCode.None));
        Assert.Null(ReasonCatalogue.Link(ReasonCode.Unknown));
        Assert.Null(ReasonCatalogue.ConsoleLine(ReasonCode.None));
        Assert.Null(BlockedNextSteps.Link(BlockedReasonCode.Unknown));
        Assert.Null(BlockedNextSteps.ConsoleLine(BlockedReasonCode.Unknown));
    }

    [Fact]
    public async Task AGateStepThatDidNotPassPrintsItsNextLineAndAPassingOneDoesNot()
    {
        using StringWriter output = new();

        await GateStepText.WriteAsync(output, new GateStep("build", 1, Verdict.Fail, ReasonCode.BuildFailed, 1200, "Program.cs(1,1): error CS1002: ; expected"));
        await GateStepText.WriteAsync(output, new GateStep("format", 0, Verdict.Pass, ReasonCode.None, 5, string.Empty));

        Assert.Equal(
            $"build: FAIL (1200 ms)\n  Program.cs(1,1): error CS1002: ; expected\n  Next (BUILD_FAILED): {ReasonCatalogue.Next(ReasonCode.BuildFailed)} See {ReasonCatalogue.GuideUrl}#build_failed\nformat: PASS (5 ms)\n",
            output.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>The anchor GitHub makes from a heading: lower case, punctuation other than <c>-</c> and <c>_</c> dropped, spaces as <c>-</c>.</summary>
    private static string Slug(string heading) =>
        new([.. heading.Trim().ToLowerInvariant().Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or ' ').Select(character => character == ' ' ? '-' : character)]);

    [GeneratedRegex("^#{1,6} (?<text>.+)$")]
    private static partial Regex Heading();
}
