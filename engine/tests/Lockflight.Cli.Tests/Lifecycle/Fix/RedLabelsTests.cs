using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Fix;

namespace Lockflight.Cli.Tests.Lifecycle.Fix;

public sealed class RedLabelsTests
{
    [Fact]
    public void ABranchWithoutRedsGetsLabelOne() =>
        Assert.Equal(1, RedLabels.Choose(Select([Commit('a')], 10), redo: null, 10).Label);

    [Fact]
    public void LabelsOneAndTwoGiveThree() =>
        Assert.Equal(3, RedLabels.Choose(Select([Commit('a', "1"), Commit('b', "2")], 10), redo: null, 10).Label);

    [Fact]
    public void ASupersededLabelAndAMalformedTrailerAreScannedSoNoLabelIsReused()
    {
        RedCommitSelection reds = Select([Commit('a', "3"), Commit('b', "x"), Commit('c', "1"), Commit('d', "3")], 10);

        Assert.Equal(4, RedLabels.Choose(reds, redo: null, 10).Label);
        Assert.Contains(reds.All, red => red.Superseded);
        Assert.Contains(reds.All, red => red.Malformed is not null);
    }

    [Fact]
    public void TheCapCountsCyclesNotLabelNumbers()
    {
        RedCommitSelection reds = Select([Commit('a', "1"), Commit('b', "5")], 10);

        RedLabelChoice refused = RedLabels.Choose(reds, redo: null, maxRedCommits: 2);
        RedLabelChoice allowed = RedLabels.Choose(reds, redo: null, maxRedCommits: 3);

        Assert.Null(refused.Label);
        Assert.Equal(ExitCode.Fail, refused.Refusal);
        Assert.Contains("ci.maxRedCommits (2)", refused.Why, StringComparison.Ordinal);
        Assert.Equal(6, allowed.Label);
    }

    [Fact]
    public void ARedoReusesItsLabelAndAddsNoCycleToTheCap()
    {
        RedLabelChoice choice = RedLabels.Choose(Select([Commit('a', "1"), Commit('b', "2")], 10), redo: 1, maxRedCommits: 2);

        Assert.Equal(1, choice.Label);
    }

    [Fact]
    public void ARedoOfALabelTheBranchDoesNotHaveIsAConfigurationError()
    {
        RedLabelChoice choice = RedLabels.Choose(Select([Commit('a', "1"), Commit('b', "2")], 10), redo: 7, maxRedCommits: 10);

        Assert.Null(choice.Label);
        Assert.Equal(ExitCode.Config, choice.Refusal);
        Assert.Contains("the labels here are 1, 2", choice.Why, StringComparison.Ordinal);
    }

    private static RedCommitSelection Select(IReadOnlyList<CommitTrailers> history, int max) => RedCommitSelector.Select(history, max);

    private static CommitTrailers Commit(char letter, params string[] red) => new(new string(letter, 40), [], "2026-11-06T10:00:00+00:00", red, [], $"commit {letter}");
}
