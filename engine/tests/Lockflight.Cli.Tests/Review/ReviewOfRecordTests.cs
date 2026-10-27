using Lockflight.Cli.Config;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReviewOfRecordTests
{
    private const string Commit = "1111111111111111111111111111111111111111";
    private const string OtherCommit = "2222222222222222222222222222222222222222";

    [Fact]
    public void TheManifestsModelAndBudgetAgainstTheMergeBaseMakeTheReviewOfRecord() =>
        Assert.Empty(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3.00m, new ReviewBase("main", Commit, "main", Commit)));

    [Fact]
    public void AnotherModelIsNotOfRecord() =>
        Assert.Contains("model sonnet is not the review model opus", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "sonnet", 3m, new ReviewBase("main", Commit, "main", Commit))), StringComparison.Ordinal);

    [Fact]
    public void AnotherBudgetIsNotOfRecord() =>
        Assert.Contains("budget $1.00 is not the review budget $3.00", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 1m, new ReviewBase("main", Commit, "main", Commit))), StringComparison.Ordinal);

    [Fact]
    public void TheManifestsSettingsReplaceTheDefaults() =>
        Assert.Empty(ReviewOfRecord.Reasons(new ReviewSettings("sonnet", 1.50m), "sonnet", 1.5m, new ReviewBase("main", Commit, "main", Commit)));

    [Fact]
    public void ABaseThatMovedOnIsNotOfRecord() =>
        Assert.Contains("is not the merge base of HEAD and the target main", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3m, new ReviewBase("main", OtherCommit, "main", Commit))), StringComparison.Ordinal);

    [Fact]
    public void ABaseWithoutAMergeBaseIsNotOfRecord() =>
        Assert.Contains("no merge base", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3m, new ReviewBase("main", Commit, "main", null))), StringComparison.Ordinal);
}
