using Lockflight.Cli.Review;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Review;

public sealed class FixChecksTests
{
    private const string Reproduction = "Shop.Tests:LockflightReproductions.Finding1.CallerCancellationSurfaces";
    private const string OwnTest = "Shop.Tests:Shop.Tests.QuoteTests.CallerCancellationKeepsTheQuote";

    private static readonly ReviewFinding Confirmed = new ReviewFinding(
        FindingSeverity.Major, "src/Quotes.cs", 12, "FR-003", "Caller cancellation is swallowed", "x", "[Fact]\npublic void CallerCancellationSurfaces() { }", FindingOutcome.Confirmed)
        { Fix = "--- a/src/Quotes.cs\n+++ b/src/Quotes.cs\n", Reproduced = ReproductionResult.Failed };

    [Fact]
    public void AFixThatMakesTheReproductionPassAndKeepsTheOwnTestsGreenHolds()
    {
        FixCheckVerdict verdict = FixChecks.Judge([Run(Reproduction, TestOutcome.Pass), Run(OwnTest, TestOutcome.Pass)], Reproduction, [OwnTest]);

        ReviewFinding checkedFinding = FixChecks.Apply(Confirmed, verdict);

        Assert.Equal(FixCheckResult.Held, verdict.Result);
        Assert.Equal(FindingOutcome.Confirmed, checkedFinding.Outcome);
        Assert.True(checkedFinding.FixChecked);
    }

    [Fact]
    public void AFixThatBreaksOneOfTheChangesOwnTestsContestsTheFindingAndNamesTheTest()
    {
        FixCheckVerdict verdict = FixChecks.Judge([Run(Reproduction, TestOutcome.Pass), Run(OwnTest, TestOutcome.Fail)], Reproduction, [OwnTest]);

        ReviewFinding contested = FixChecks.Apply(Confirmed, verdict);

        Assert.Equal(FixCheckResult.BreaksOwnTest, verdict.Result);
        Assert.Equal(FindingOutcome.Contested, contested.Outcome);
        Assert.Equal(["Shop.Tests.QuoteTests.CallerCancellationKeepsTheQuote"], contested.ConflictingTests);
        Assert.False(contested.FixChecked);
    }

    [Fact]
    public void AFixThatDoesNotApplyLeavesTheFindingConfirmedButNotFixChecked()
    {
        ReviewFinding finding = FixChecks.Apply(Confirmed, new FixCheckVerdict(FixCheckResult.DidNotApply, [], "src/Quotes.cs: hunk 1 does not match the file"));

        Assert.Equal(FindingOutcome.Confirmed, finding.Outcome);
        Assert.Equal(FixCheckResult.DidNotApply, finding.FixCheck);
        Assert.False(finding.FixChecked);
        Assert.Equal("src/Quotes.cs: hunk 1 does not match the file", finding.FixCheckErrors);
    }

    [Fact]
    public void AFixThatDoesNotBuildLeavesTheFindingConfirmedButNotFixChecked()
    {
        FixCheckVerdict verdict = FixChecks.BuildFailure([new CompileError("Quotes.cs", 4, 9, "CS0103", "x")], ["tests/Quotes.Tests/QuoteTests.cs"]);

        ReviewFinding finding = FixChecks.Apply(Confirmed, verdict);

        Assert.Equal(FixCheckResult.DidNotBuild, verdict.Result);
        Assert.Equal(FindingOutcome.Confirmed, finding.Outcome);
        Assert.False(finding.FixChecked);
    }

    [Fact]
    public void AFixWhoseBuildFailsOnlyInTheChangesOwnTestFilesContestsTheFindingAndNamesThem()
    {
        FixCheckVerdict verdict = FixChecks.BuildFailure(
            [new CompileError("QuoteTests.cs", 12, 9, "CS0029", "x"), new CompileError("QuoteTests.cs", 20, 9, "CS1503", "y")],
            ["tests/Quotes.Tests/QuoteTests.cs", "tests/Quotes.Tests/OtherTests.cs"]);

        ReviewFinding contested = FixChecks.Apply(Confirmed, verdict);

        Assert.Equal(FixCheckResult.BreaksOwnTestBuild, verdict.Result);
        Assert.Equal(FindingOutcome.Contested, contested.Outcome);
        Assert.Equal(["tests/Quotes.Tests/QuoteTests.cs"], contested.ConflictingTests);
    }

    [Fact]
    public void AFixWhoseBuildAlsoFailsElsewhereIsNotContested() =>
        Assert.Equal(
            FixCheckResult.DidNotBuild,
            FixChecks.BuildFailure([new CompileError("QuoteTests.cs", 12, 9, "CS0029", "x"), new CompileError("Finding1.cs", 3, 1, "CS0029", "y")], ["tests/Quotes.Tests/QuoteTests.cs"]).Result);

    [Fact]
    public void AFixThatLeavesTheReproductionFailingKeepsTheFindingConfirmedButNotFixChecked()
    {
        FixCheckVerdict verdict = FixChecks.Judge([Run(Reproduction, TestOutcome.Fail), Run(OwnTest, TestOutcome.Fail)], Reproduction, [OwnTest]);

        ReviewFinding finding = FixChecks.Apply(Confirmed, verdict);

        Assert.Equal(FixCheckResult.DidNotFix, verdict.Result);
        Assert.Equal(FindingOutcome.Confirmed, finding.Outcome);
        Assert.False(finding.FixChecked);
    }

    [Fact]
    public void AReproductionTheRunDidNotReportLeavesTheFixUnchecked() =>
        Assert.Equal(FixCheckResult.NotRun, FixChecks.Judge([Run(OwnTest, TestOutcome.Pass)], Reproduction, [OwnTest]).Result);

    [Fact]
    public void AFindingWithoutAFixIsNotFixChecked() =>
        Assert.False((Confirmed with { Fix = null }).FixChecked);

    private static ExecutedTest Run(string key, TestOutcome outcome) => new(key, key, outcome, null, null);
}
