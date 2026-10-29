using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

public sealed class CommitTrailersTests
{
    private static readonly string First = new('a', 40);
    private static readonly string Second = new('b', 40);

    [Fact]
    public void EachCommitHasItsParentsTrailersAndSubject()
    {
        string log = $"{First}\0\02026-10-29T10:00:00+00:00\01\u00012\0\0first\0{Second}\0{First}\02026-10-29T11:00:00+00:00\0\03\0second: with \u0001 in it\0";

        IReadOnlyList<CommitTrailers>? commits = CommitTrailers.ParseLog(log);

        Assert.NotNull(commits);
        Assert.Equal(2, commits.Count);
        Assert.Equal(["1", "2"], commits[0].Red);
        Assert.Empty(commits[0].Parents);
        Assert.Equal([First], commits[1].Parents);
        Assert.Equal(["3"], commits[1].Amend);
        Assert.Equal("second: with \u0001 in it", commits[1].Subject);
    }

    [Fact]
    public void OutputOfAnotherShapeIsUnreadable()
    {
        Assert.Null(CommitTrailers.ParseLog($"{First}\0\0x\0"));
        Assert.Null(CommitTrailers.ParseLog("not-a-commit\0\0t\0\0\0subject\0"));
    }

    [Fact]
    public void NoCommitsIsAnEmptyHistory() => Assert.Empty(CommitTrailers.ParseLog(string.Empty)!);
}
