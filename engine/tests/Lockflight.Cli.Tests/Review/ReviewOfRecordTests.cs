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
        Assert.Empty(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3.00m, new ReviewBase("main", Commit, "main", Commit), Whole));

    [Fact]
    public void AnotherModelIsNotOfRecord() =>
        Assert.Contains("model sonnet is not the review model opus", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "sonnet", 3m, new ReviewBase("main", Commit, "main", Commit), Whole)), StringComparison.Ordinal);

    [Fact]
    public void AnotherBudgetIsNotOfRecord() =>
        Assert.Contains("budget $1.00 is not the review budget $3.00", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 1m, new ReviewBase("main", Commit, "main", Commit), Whole)), StringComparison.Ordinal);

    [Fact]
    public void TheManifestsSettingsReplaceTheDefaults() =>
        Assert.Empty(ReviewOfRecord.Reasons(new ReviewSettings("sonnet", 1.50m), "sonnet", 1.5m, new ReviewBase("main", Commit, "main", Commit), Whole));

    [Fact]
    public void ABaseThatMovedOnIsNotOfRecord() =>
        Assert.Contains("is not the merge base of HEAD and the target main", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3m, new ReviewBase("main", OtherCommit, "main", Commit), Whole)), StringComparison.Ordinal);

    [Fact]
    public void ABaseWithoutAMergeBaseIsNotOfRecord() =>
        Assert.Contains("no merge base", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3m, new ReviewBase("main", Commit, "main", null), Whole)), StringComparison.Ordinal);

    [Fact]
    public void ADiffTheReviewerDidNotSeeWholeIsNotOfRecord()
    {
        ReviewDiff cut = ReviewDiff.Fit("diff --git a/src/Orders.cs b/src/Orders.cs\n+line\ndiff --git a/src/Prices.cs b/src/Prices.cs\n" + new string('+', 100) + "\n", 50);

        string reason = Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3m, new ReviewBase("main", Commit, "main", Commit), cut));

        Assert.Contains("the reviewer did not see the whole change", reason, StringComparison.Ordinal);
        Assert.Contains("1 of 2 changed files not shown in full: src/Prices.cs", reason, StringComparison.Ordinal);
        Assert.Contains("split the change", reason, StringComparison.Ordinal);
    }
}
