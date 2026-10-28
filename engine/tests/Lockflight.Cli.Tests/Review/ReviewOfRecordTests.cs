using Lockflight.Cli.Config;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReviewOfRecordTests
{
    private const string Commit = "1111111111111111111111111111111111111111";
    private const string OtherCommit = "2222222222222222222222222222222222222222";

    private static readonly ReviewDiff Whole = ReviewDiff.Fit("diff --git a/src/Orders.cs b/src/Orders.cs\n+line\n", 1_000);

    [Fact]
    public void TheManifestsModelAndBudgetAgainstTheMergeBaseMakeTheReviewOfRecord() =>
        Assert.Empty(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3.00m, new ReviewBase("main", Commit, "main", Commit), Whole, ReviewedTree.Clean));

    [Fact]
    public void AnotherModelIsNotOfRecord() =>
        Assert.Contains("model sonnet is not the review model opus", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "sonnet", 3m, new ReviewBase("main", Commit, "main", Commit), Whole, ReviewedTree.Clean)), StringComparison.Ordinal);

    [Fact]
    public void AnotherBudgetIsNotOfRecord() =>
        Assert.Contains("budget $1.00 is not the review budget $3.00", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 1m, new ReviewBase("main", Commit, "main", Commit), Whole, ReviewedTree.Clean)), StringComparison.Ordinal);

    [Fact]
    public void TheManifestsSettingsReplaceTheDefaults() =>
        Assert.Empty(ReviewOfRecord.Reasons(new ReviewSettings("sonnet", 1.50m), "sonnet", 1.5m, new ReviewBase("main", Commit, "main", Commit), Whole, ReviewedTree.Clean));

    [Fact]
    public void ABaseThatMovedOnIsNotOfRecord() =>
        Assert.Contains("is not the merge base of HEAD and the target main", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3m, new ReviewBase("main", OtherCommit, "main", Commit), Whole, ReviewedTree.Clean)), StringComparison.Ordinal);

    [Fact]
    public void ABaseWithoutAMergeBaseIsNotOfRecord() =>
        Assert.Contains("no merge base", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3m, new ReviewBase("main", Commit, "main", null), Whole, ReviewedTree.Clean)), StringComparison.Ordinal);

    [Fact]
    public void ADiffTheReviewerDidNotSeeWholeIsNotOfRecord()
    {
        ReviewDiff cut = ReviewDiff.Fit("diff --git a/src/Orders.cs b/src/Orders.cs\n+line\ndiff --git a/src/Prices.cs b/src/Prices.cs\n" + new string('+', 100) + "\n", 50);

        string reason = Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3m, new ReviewBase("main", Commit, "main", Commit), cut, ReviewedTree.Clean));

        Assert.Contains("the reviewer did not see the whole change", reason, StringComparison.Ordinal);
        Assert.Contains("1 of 2 changed files not shown in full: src/Prices.cs", reason, StringComparison.Ordinal);
        Assert.Contains("split the change", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void UncommittedChangesAreNotOfRecordAndSayToCommitOrStash()
    {
        ReviewedTree dirty = new(true, ["src/Orders.cs", "src/Prices.cs", "src/Stock.cs", "src/Tax.cs"], []);

        string reason = Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3m, new ReviewBase("main", Commit, "main", Commit), Whole, dirty));

        Assert.Contains("the working tree has changes that are not committed", reason, StringComparison.Ordinal);
        Assert.Contains("4 uncommitted: src/Orders.cs, src/Prices.cs, src/Stock.cs and 1 more", reason, StringComparison.Ordinal);
        Assert.Contains("commit them, or stash them", reason, StringComparison.Ordinal);
        Assert.Contains("ignored files", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUntrackedFileIsNotOfRecordAndTheReasonSaysTheReviewerNeverSawIt()
    {
        ReviewedTree dirty = new(true, [], ["src/New.cs"]);

        string reason = Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3m, new ReviewBase("main", Commit, "main", Commit), Whole, dirty));

        Assert.Contains("1 untracked, which the reviewer was not shown: src/New.cs", reason, StringComparison.Ordinal);
        Assert.Contains("git stash -u", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ATreeGitCouldNotCheckIsNotOfRecord() =>
        Assert.Contains(
            "git could not list the uncommitted changes",
            Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3m, new ReviewBase("main", Commit, "main", Commit), Whole, ReviewedTree.Unchecked)),
            StringComparison.Ordinal);
}
