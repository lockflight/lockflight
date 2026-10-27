using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class FindingLifecycleTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnOpenFindingTheNewRunDoesNotRaiseIsCarriedForward()
    {
        ReviewFinding open = Major("Discount off by one", FindingOutcome.Unverified) with { State = FindingState.Open };

        IReadOnlyList<ReviewFinding> findings = FindingLifecycle.Carry([], History(open));

        ReviewFinding carried = Assert.Single(findings);
        Assert.True(carried.Carried);
        Assert.Equal(open.Id, carried.Id);
    }

    [Fact]
    public void AnOpenFindingTheNewRunRaisesAgainIsNotCarriedTwice()
    {
        ReviewFinding open = Major("Discount off by one", FindingOutcome.Unverified) with { State = FindingState.Open };

        IReadOnlyList<ReviewFinding> findings = FindingLifecycle.Carry([Major("Discount off by one", FindingOutcome.Reported)], History(open));

        Assert.False(Assert.Single(findings).Carried);
    }

    [Fact]
    public void AnOpenFindingRaisedAgainAsMinorKeepsItsSeverityAndReproduction()
    {
        ReviewFinding open = Major("Discount off by one", FindingOutcome.Confirmed) with { State = FindingState.Open, Reproduction = "[Fact] public void R() {}" };

        ReviewFinding raised = Assert.Single(FindingLifecycle.Carry([open with { Severity = FindingSeverity.Minor, Reproduction = null, State = null }], History(open)));

        Assert.Equal(FindingSeverity.Major, raised.Severity);
        Assert.Equal(open.Reproduction, raised.Reproduction);
        Assert.False(raised.Carried);
    }

    [Fact]
    public void AnOpenFindingRaisedAgainAsBlockerBecomesABlocker()
    {
        ReviewFinding open = Major("Discount off by one", FindingOutcome.Unverified) with { State = FindingState.Open };

        ReviewFinding raised = Assert.Single(FindingLifecycle.Carry([open with { Severity = FindingSeverity.Blocker, State = null }], History(open)));

        Assert.Equal(FindingSeverity.Blocker, raised.Severity);
    }

    [Fact]
    public void AConfirmedFindingRaisedAgainAsMinorStaysOpenAndConfirmed()
    {
        ReviewFinding open = Major("Discount off by one", FindingOutcome.Confirmed) with { State = FindingState.Open };
        ReviewFinding raised = Assert.Single(FindingLifecycle.Carry([open with { Severity = FindingSeverity.Nit, Outcome = FindingOutcome.Reported, State = null }], History(open)));

        ReviewFinding resolved = Assert.Single(FindingLifecycle.Resolve([raised with { Outcome = FindingOutcome.Unverified }], History(open)));

        Assert.Equal(FindingState.Open, resolved.State);
        Assert.Equal(FindingOutcome.Confirmed, resolved.Outcome);
    }

    [Fact]
    public void AnOpenFindingRaisedAgainWithANewReproductionKeepsTheEarlierOnes()
    {
        ReviewFinding open = Major("Discount off by one", FindingOutcome.Confirmed) with
        {
            State = FindingState.Open,
            Reproduction = Repro("Second"),
            EarlierReproductions = [Repro("First")],
        };

        ReviewFinding raised = Assert.Single(FindingLifecycle.Carry([open with { Reproduction = Repro("Third"), EarlierReproductions = [], State = null }], History(open)));

        Assert.Equal(Repro("Third"), raised.Reproduction);
        Assert.Equal([Repro("First"), Repro("Second")], raised.EarlierReproductions);
    }

    [Fact]
    public void ARepeatedReproductionIsNotKeptTwice()
    {
        ReviewFinding open = Major("Discount off by one", FindingOutcome.Confirmed) with { State = FindingState.Open, Reproduction = Repro("Same") };

        ReviewFinding raised = Assert.Single(FindingLifecycle.Carry([open with { State = null }], History(open)));

        Assert.Empty(raised.EarlierReproductions);
    }

    [Fact]
    public void EveryKnownReproductionIsRunAsItsOwnCheck()
    {
        ReviewFinding finding = Major("Discount off by one", FindingOutcome.Reported) with { Reproduction = Repro("New"), EarlierReproductions = [Repro("Old")] };

        IReadOnlyList<ReviewFinding> checks = FindingLifecycle.WithEarlierReproductions([finding, Major("Other", FindingOutcome.Reported)]);

        Assert.Equal([Repro("New"), null, Repro("Old")], checks.Select(check => check.Reproduction));
    }

    [Theory]
    [InlineData("passed", "failed", "confirmed", "failed")]
    [InlineData("failed", "passed", "confirmed", "failed")]
    [InlineData("passed", "passed", "rejected", "passed")]
    [InlineData("passed", "did-not-compile", "unverified", "did-not-compile")]
    [InlineData("none", "passed", "unverified", "none")]
    public void AFindingIsRejectedOnlyWhenEveryKnownReproductionPasses(string newResult, string earlierResult, string expectedOutcome, string expectedResult)
    {
        ReviewFinding finding = Major("Discount off by one", FindingOutcome.Reported) with { Reproduction = Repro("New"), EarlierReproductions = [Repro("Old")] };
        IReadOnlyList<ReviewFinding> checks = FindingLifecycle.WithEarlierReproductions([finding]);

        ReviewFinding combined = Assert.Single(FindingLifecycle.CombineReproductions([finding], [Judged(checks[0], Result(newResult)), Judged(checks[1], Result(earlierResult))]));

        Assert.Equal(expectedOutcome, ReviewNames.ToName(combined.Outcome));
        Assert.Equal(expectedResult, ReviewNames.ToName(combined.Reproduced));
        Assert.Equal(Repro("New"), combined.Reproduction);
        Assert.Equal([Repro("Old")], combined.EarlierReproductions);
    }

    [Fact]
    public void ACarriedFindingWithoutAReproductionStaysOpen()
    {
        ReviewFinding open = Major("Discount off by one", FindingOutcome.Unverified) with { State = FindingState.Open };

        ReviewFinding resolved = Assert.Single(FindingLifecycle.Resolve([open with { Carried = true }], History(open)));

        Assert.Equal(FindingState.Open, resolved.State);
        Assert.Equal(FindingOutcome.Unverified, resolved.Outcome);
    }

    [Fact]
    public void AnOpenFindingWhoseReproductionNowPassesIsFixed()
    {
        ReviewFinding open = Major("Discount off by one", FindingOutcome.Confirmed) with { State = FindingState.Open };

        ReviewFinding resolved = Assert.Single(FindingLifecycle.Resolve(
            [open with { Carried = true, Outcome = FindingOutcome.Rejected, Reproduced = ReproductionResult.Passed }], History(open)));

        Assert.Equal(FindingState.Fixed, resolved.State);
    }

    [Fact]
    public void AConfirmedFindingWhoseReproductionCannotRunNowStaysConfirmed()
    {
        ReviewFinding open = Major("Discount off by one", FindingOutcome.Confirmed) with { State = FindingState.Open };

        ReviewFinding resolved = Assert.Single(FindingLifecycle.Resolve(
            [open with { Carried = true, Outcome = FindingOutcome.Unverified, Reproduced = ReproductionResult.NotRun }], History(open)));

        Assert.Equal(FindingState.Open, resolved.State);
        Assert.Equal(FindingOutcome.Confirmed, resolved.Outcome);
    }

    [Fact]
    public void ANewConfirmedFindingOpens() =>
        Assert.Equal(FindingState.Open, Assert.Single(FindingLifecycle.Resolve([Major("New", FindingOutcome.Confirmed)], History())).State);

    [Fact]
    public void ANewUnverifiedFindingOpens() =>
        Assert.Equal(FindingState.Open, Assert.Single(FindingLifecycle.Resolve([Major("New", FindingOutcome.Unverified)], History())).State);

    [Fact]
    public void ANewFindingWhoseReproductionPassesIsNotTracked() =>
        Assert.Null(Assert.Single(FindingLifecycle.Resolve([Major("New", FindingOutcome.Rejected)], History())).State);

    [Fact]
    public void AMinorFindingIsNotTracked() =>
        Assert.Null(Assert.Single(FindingLifecycle.Resolve(
            [Major("Naming", FindingOutcome.Reported) with { Severity = FindingSeverity.Minor }], History())).State);

    [Fact]
    public void ADismissedFindingRaisedAgainStaysDismissed()
    {
        ReviewFinding finding = Major("Discount off by one", FindingOutcome.Confirmed);
        ReviewHistory history = History() with
        {
            Dismissed = [new FindingDismissal(finding.Id, finding.Severity, finding.File, finding.Line, finding.Title, "dev@example.com", At, "Intended")],
        };

        Assert.Equal(FindingState.Dismissed, Assert.Single(FindingLifecycle.Resolve([finding], history)).State);
    }

    [Fact]
    public void ADismissedFindingIsNotCarried()
    {
        ReviewFinding finding = Major("Discount off by one", FindingOutcome.Confirmed);
        ReviewHistory history = History() with
        {
            Dismissed = [new FindingDismissal(finding.Id, finding.Severity, finding.File, finding.Line, finding.Title, "dev@example.com", At, "Intended")],
        };

        Assert.Empty(FindingLifecycle.Carry([], history));
    }

    private static string Repro(string name) => $"[Fact]\npublic void {name}() => Assert.True(false);";

    private static ReproductionResult Result(string name) => Enum.GetValues<ReproductionResult>().Single(result => ReviewNames.ToName(result) == name);

    private static ReviewFinding Judged(ReviewFinding finding, ReproductionResult result) => finding with
    {
        Reproduced = result,
        Outcome = result switch
        {
            ReproductionResult.Failed => FindingOutcome.Confirmed,
            ReproductionResult.Passed => FindingOutcome.Rejected,
            _ => FindingOutcome.Unverified,
        },
    };

    private static ReviewHistory History(params ReviewFinding[] open) =>
        ReviewHistory.Empty with { Open = [.. open.Select(finding => new TrackedFinding(finding, "20261027-110000", "20261027-110000"))] };

    private static ReviewFinding Major(string title, FindingOutcome outcome) =>
        new ReviewFinding(FindingSeverity.Major, "src/Orders.cs", 12, "FR-001", title, "evidence", null, outcome)
        {
            Id = FindingIds.Of(ReviewNames.GeneralDimension, "src/Orders.cs", 12, title),
        };
}
