using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

public sealed class RedCommitSelectorTests
{
    [Fact]
    public void EachWellFormedTrailerIsACycleInTheCommitsOrder()
    {
        RedCommitSelection selection = RedCommitSelector.Select([Commit('a', "1"), Commit('b'), Commit('c', "2")], maxRedCommits: 10);

        Assert.Equal([Id('a'), Id('c')], selection.Cycles.Select(red => red.Commit));
        Assert.Equal(["1", "2"], selection.Cycles.Select(red => red.Label));
        Assert.False(selection.OverCap);
        Assert.False(selection.LabelsOutOfOrder);
    }

    [Fact]
    public void TheLastCommitWithALabelIsTheCycleAndEarlierOnesAreSuperseded()
    {
        RedCommitSelection selection = RedCommitSelector.Select([Commit('a', "1"), Commit('b', "2"), Commit('c', "1")], maxRedCommits: 10);

        Assert.Equal([Id('b'), Id('c')], selection.Cycles.Select(red => red.Commit));
        Assert.True(selection.All.Single(red => red.Commit == Id('a')).Superseded);
        Assert.True(selection.LabelsOutOfOrder);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1234567")]
    public void AValueThatIsNotACycleNumberIsMalformedAndNotARed(string value)
    {
        RedCommitSelection selection = RedCommitSelector.Select([Commit('a', value)], maxRedCommits: 10);

        RedCommit red = Assert.Single(selection.All);
        Assert.Contains("is not a cycle number", red.Malformed, StringComparison.Ordinal);
        Assert.Empty(selection.Cycles);
    }

    [Fact]
    public void TheFeatureAndCycleFormIsReservedAndNotARedYet()
    {
        RedCommit red = Assert.Single(RedCommitSelector.Select([Commit('a', "012-checkout/2")], maxRedCommits: 10).All);

        Assert.Contains("reserved", red.Malformed, StringComparison.Ordinal);
        Assert.False(red.IsCycle);
    }

    [Fact]
    public void TwoRedTrailersInOneCommitAreMalformed()
    {
        RedCommit red = Assert.Single(RedCommitSelector.Select([Commit('a', "1", "2")], maxRedCommits: 10).All);

        Assert.Contains("2 Lockflight-Red trailers", red.Malformed, StringComparison.Ordinal);
    }

    [Fact]
    public void MoreCyclesThanTheCapAreFlagged()
    {
        RedCommitSelection selection = RedCommitSelector.Select([Commit('a', "1"), Commit('b', "2"), Commit('c', "3")], maxRedCommits: 2);

        Assert.True(selection.OverCap);
    }

    [Fact]
    public void AmendTrailersAreListedButMakeNoRed()
    {
        CommitTrailers amend = Commit('a') with { Amend = ["1"] };

        RedCommitSelection selection = RedCommitSelector.Select([Commit('b', "1"), amend], maxRedCommits: 10);

        Assert.Equal([Id('a')], selection.AmendCommits);
        Assert.Single(selection.Cycles);
    }

    private static string Id(char letter) => new(letter, 40);

    private static CommitTrailers Commit(char letter, params string[] red) => new(Id(letter), [], "2026-10-29T10:00:00+00:00", red, [], $"commit {letter}");
}
