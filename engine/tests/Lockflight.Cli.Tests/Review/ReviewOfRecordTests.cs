using Lockflight.Cli.Config;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReviewOfRecordTests
{
    private const string Commit = "1111111111111111111111111111111111111111";
    private const string OtherCommit = "2222222222222222222222222222222222222222";

    private static readonly string NoTree = Path.Combine(Path.GetTempPath(), "lockflight-no-tree");
    private static readonly ReviewDiff Whole = ReviewDiff.Fit("diff --git a/src/Orders.cs b/src/Orders.cs\n+line\n", ReviewSettings.DefaultMaxDiffCharacters, NoTree);

    [Fact]
    public void TheManifestsModelAndBudgetAgainstTheMergeBaseMakeTheReviewOfRecord() =>
        Assert.Empty(ReviewOfRecord.Reasons(ReviewSettings.Default, "sonnet", 3.00m, new ReviewBase("main", Commit, "main", Commit), Whole, ReviewedTree.Clean));

    [Fact]
    public void AnotherModelIsNotOfRecord() =>
        Assert.Contains("model opus is not the review model sonnet", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "opus", 3m, new ReviewBase("main", Commit, "main", Commit), Whole, ReviewedTree.Clean)), StringComparison.Ordinal);

    [Fact]
    public void AnotherBudgetIsNotOfRecord() =>
        Assert.Contains("budget $1.00 is not the review budget $3.00", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "sonnet", 1m, new ReviewBase("main", Commit, "main", Commit), Whole, ReviewedTree.Clean)), StringComparison.Ordinal);

    [Fact]
    public void TheManifestsSettingsReplaceTheDefaults() =>
        Assert.Empty(ReviewOfRecord.Reasons(new ReviewSettings("opus", 1.50m), "opus", 1.5m, new ReviewBase("main", Commit, "main", Commit), Whole, ReviewedTree.Clean));

    [Fact]
    public void ABaseThatMovedOnIsNotOfRecord() =>
        Assert.Contains("is not the merge base of HEAD and the target main", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "sonnet", 3m, new ReviewBase("main", OtherCommit, "main", Commit), Whole, ReviewedTree.Clean)), StringComparison.Ordinal);

    [Fact]
    public void ABaseWithoutAMergeBaseIsNotOfRecord() =>
        Assert.Contains("no merge base", Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "sonnet", 3m, new ReviewBase("main", Commit, "main", null), Whole, ReviewedTree.Clean)), StringComparison.Ordinal);

    [Fact]
    public void ADiffTheReviewerDidNotSeeWholeIsNotOfRecord()
    {
        ReviewDiff cut = ReviewDiff.Fit("diff --git a/src/Orders.cs b/src/Orders.cs\n+line\ndiff --git a/src/Prices.cs b/src/Prices.cs\n" + new string('+', 100) + "\n", 50, NoTree);

        string reason = Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default with { MaxDiffCharacters = 50 }, "sonnet", 3m, new ReviewBase("main", Commit, "main", Commit), cut, ReviewedTree.Clean));

        Assert.Contains("the reviewer did not see the whole change", reason, StringComparison.Ordinal);
        Assert.Contains("1 of 2 changed files not shown in full: src/Prices.cs", reason, StringComparison.Ordinal);
        Assert.Contains("split the change", reason, StringComparison.Ordinal);
        Assert.Contains("raise review.maxDiffCharacters (at most 400000", reason, StringComparison.Ordinal);
        Assert.Contains("review.maxUsd may need raising", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADiffFittedToAnotherLimitThanTheManifestsIsNotOfRecord()
    {
        ReviewSettings settings = ReviewSettings.Default with { MaxDiffCharacters = 200_000 };

        string reason = Assert.Single(ReviewOfRecord.Reasons(settings, "sonnet", 3m, new ReviewBase("main", Commit, "main", Commit), Whole, ReviewedTree.Clean));

        Assert.Contains("at most 120000 characters of the diff, not the review limit 200000 (review.maxDiffCharacters)", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADiffFittedToTheManifestsRaisedLimitIsOfRecord()
    {
        ReviewSettings settings = ReviewSettings.Default with { MaxDiffCharacters = 200_000 };
        ReviewDiff fitted = ReviewDiff.Fit("diff --git a/src/Orders.cs b/src/Orders.cs\n+line\n", settings.MaxDiffCharacters, NoTree);

        Assert.Empty(ReviewOfRecord.Reasons(settings, "sonnet", 3m, new ReviewBase("main", Commit, "main", Commit), fitted, ReviewedTree.Clean));
    }

    [Fact]
    public void TextFilesShownAsBinaryAreNotOfRecordAndNamed()
    {
        ReviewDiff hidden = Whole.WithTextShownAsBinary(["src/Orders.cs", "src/Prices.cs"]);

        string reason = Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "sonnet", 3m, new ReviewBase("main", Commit, "main", Commit), hidden, ReviewedTree.Clean));

        Assert.Contains("git showed 2 text files as binary (src/Orders.cs, src/Prices.cs), so the reviewer did not see their change", reason, StringComparison.Ordinal);
        Assert.Contains("read them: src/Orders.cs, src/Prices.cs]", hidden.Shown, StringComparison.Ordinal);
    }

    [Fact]
    public void UncommittedChangesAreNotOfRecordAndSayToCommitOrStash()
    {
        ReviewedTree dirty = new(true, ["src/Orders.cs", "src/Prices.cs", "src/Stock.cs", "src/Tax.cs"], []);

        string reason = Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "sonnet", 3m, new ReviewBase("main", Commit, "main", Commit), Whole, dirty));

        Assert.Contains("the working tree has changes that are not committed", reason, StringComparison.Ordinal);
        Assert.Contains("4 uncommitted: src/Orders.cs, src/Prices.cs, src/Stock.cs and 1 more", reason, StringComparison.Ordinal);
        Assert.Contains("commit them, or stash them", reason, StringComparison.Ordinal);
        Assert.Contains("ignored files", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUntrackedFileIsNotOfRecordAndTheReasonSaysTheReviewerNeverSawIt()
    {
        ReviewedTree dirty = new(true, [], ["src/New.cs"]);

        string reason = Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "sonnet", 3m, new ReviewBase("main", Commit, "main", Commit), Whole, dirty));

        Assert.Contains("1 untracked, which the reviewer was not shown: src/New.cs", reason, StringComparison.Ordinal);
        Assert.Contains("git stash -u", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ATreeGitCouldNotCheckIsNotOfRecord() =>
        Assert.Contains(
            "git could not list the uncommitted changes",
            Assert.Single(ReviewOfRecord.Reasons(ReviewSettings.Default, "sonnet", 3m, new ReviewBase("main", Commit, "main", Commit), Whole, ReviewedTree.Unchecked)),
            StringComparison.Ordinal);
}
