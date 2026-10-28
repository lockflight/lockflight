using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class EscalationsTests
{
    private const string Reproduction = "[Fact]\npublic void TotalAppliesTheDiscount() => Assert.Equal(90m, PriceCalculator.Total(100m, 10m));";
    private static readonly HashSet<string> Repairable = ["f1"];

    [Theory]
    [InlineData("The total MUST apply the discount.", "MUST")]
    [InlineData("The total SHALL NOT exceed the price.", "SHALL")]
    [InlineData("A refund MUST NOT be issued twice.", "MUST")]
    public void AnUpperCaseMustOrShallIsTheKeyword(string quote, string keyword) => Assert.Equal(keyword, Escalations.Keyword(quote));

    [Theory]
    [InlineData("The total must apply the discount.")]
    [InlineData("The total Shall apply the discount.")]
    [InlineData("The MUSTER point is shown on the map.")]
    [InlineData("The total applies the discount.")]
    [InlineData(null)]
    public void AQuoteWithoutAWholeUpperCaseMustOrShallHasNoKeyword(string? quote) => Assert.Null(Escalations.Keyword(quote));

    [Fact]
    public void AMinorFindingWithAnAnchoredMustAndARunnableReproductionIsACandidate() => Assert.True(Escalations.IsCandidate(Candidate()));

    [Fact]
    public void ANitFindingWithAnAnchoredMustIsACandidate() => Assert.True(Escalations.IsCandidate(Candidate() with { Severity = FindingSeverity.Nit }));

    [Fact]
    public void AFindingWhoseQuoteIsNotAnchoredIsNotACandidate() =>
        Assert.False(Escalations.IsCandidate(Candidate() with { SpecAnchor = SpecAnchor.NotAnchored }));

    [Fact]
    public void AFindingWhoseAnchoredQuoteHasNoMustOrShallIsNotACandidate() =>
        Assert.False(Escalations.IsCandidate(Candidate() with { SpecQuote = "The total applies the discount." }));

    [Fact]
    public void AFindingWithoutARunnableReproductionIsNotACandidate()
    {
        Assert.False(Escalations.IsCandidate(Candidate() with { Reproduction = null }));
        Assert.False(Escalations.IsCandidate(Candidate() with { Reproduction = "Assert.True(false);" }));
    }

    [Fact]
    public void AMajorOrCarriedFindingIsNotACandidate()
    {
        Assert.False(Escalations.IsCandidate(Candidate() with { Severity = FindingSeverity.Major }));
        Assert.False(Escalations.IsCandidate(Candidate() with { Carried = true }));
    }

    [Fact]
    public void ACandidateIsProposedAsMajorAndRemembersItsSeverity()
    {
        ReviewFinding proposed = Escalations.Propose(Candidate());

        Assert.Equal(FindingSeverity.Major, proposed.Severity);
        Assert.Equal(FindingSeverity.Minor, proposed.OriginalSeverity);
        Assert.True(proposed.NeedsReproduction);
    }

    [Fact]
    public void AFindingThatIsNotACandidateIsNotProposed()
    {
        ReviewFinding finding = Candidate() with { SpecAnchor = SpecAnchor.NotAnchored };

        Assert.Same(finding, Escalations.Propose(finding));
    }

    [Fact]
    public void AProposedFindingWhoseReproductionFailedIsEscalated()
    {
        ReviewFinding settled = Escalations.Settle(Judged(FindingOutcome.Confirmed, ReproductionResult.Failed), Repairable);

        Assert.Equal(FindingSeverity.Major, settled.Severity);
        Assert.Equal(FindingSeverity.Minor, settled.OriginalSeverity);
        Assert.Equal(SeverityEscalation.Escalated, settled.Escalation);
        Assert.Equal("escalated: anchored MUST, reproduced", ReviewNames.Describe(settled));
    }

    [Fact]
    public void AProposedFindingWhoseReproductionPassesStaysMinorAndReported()
    {
        ReviewFinding settled = Escalations.Settle(Judged(FindingOutcome.Rejected, ReproductionResult.Passed), Repairable);

        Assert.Equal(FindingSeverity.Minor, settled.Severity);
        Assert.Null(settled.OriginalSeverity);
        Assert.Equal(FindingOutcome.Reported, settled.Outcome);
        Assert.Equal(SeverityEscalation.ReproductionPassed, settled.Escalation);
        Assert.False(settled.NeedsReproduction);
    }

    [Fact]
    public void AProposedFindingWhoseRepairedReproductionPassesStaysMinor() =>
        Assert.Equal(SeverityEscalation.ReproductionPassed, Escalations.Settle(Judged(FindingOutcome.Unverified, ReproductionResult.RepairedPassed, 2), Repairable).Escalation);

    [Fact]
    public void AProposedFindingWhoseReproductionWasNotRepairedForLackOfBudgetSaysSo()
    {
        ReviewFinding settled = Escalations.Settle(Judged(FindingOutcome.Unverified, ReproductionResult.DidNotCompile), Repairable);

        Assert.Equal(FindingSeverity.Minor, settled.Severity);
        Assert.Equal(SeverityEscalation.BudgetExhausted, settled.Escalation);
        Assert.Contains("budget was spent", ReviewNames.Describe(settled), StringComparison.Ordinal);
    }

    [Fact]
    public void AProposedFindingWhoseRepairedReproductionStillDoesNotCompileStaysMinor() =>
        Assert.Equal(SeverityEscalation.ReproductionDidNotCompile, Escalations.Settle(Judged(FindingOutcome.Unverified, ReproductionResult.DidNotCompile, 2), Repairable).Escalation);

    [Fact]
    public void AProposedFindingWhoseReproductionDidNotRunStaysMinor()
    {
        Assert.Equal(SeverityEscalation.ReproductionNotRun, Escalations.Settle(Judged(FindingOutcome.Unverified, ReproductionResult.NotRun), Repairable).Escalation);
        Assert.Equal(SeverityEscalation.ReproductionNotRun, Escalations.Settle(Judged(FindingOutcome.Unverified, ReproductionResult.Inconclusive), Repairable).Escalation);
    }

    [Fact]
    public void AFindingThatWasNotProposedIsLeftAsItIs()
    {
        ReviewFinding major = Candidate() with { Severity = FindingSeverity.Major, Outcome = FindingOutcome.Unverified };

        Assert.Same(major, Escalations.Settle(major, Repairable));
    }

    [Fact]
    public void AFindingEscalatedInAnEarlierRunIsNotSettledAgain()
    {
        ReviewFinding earlier = Judged(FindingOutcome.Unverified, ReproductionResult.NotRun) with { Escalation = SeverityEscalation.Escalated };

        Assert.Same(earlier, Escalations.Settle(earlier, Repairable));
    }

    private static ReviewFinding Candidate() =>
        new(FindingSeverity.Minor, "src/Shop/PriceCalculator.cs", 5, null, "Discount ignored", "Total returns the price", Reproduction, FindingOutcome.Reported)
        {
            Id = "f1",
            SpecQuote = "The total MUST apply the discount.",
            SpecAnchor = SpecAnchor.Anchored,
        };

    private static ReviewFinding Judged(FindingOutcome outcome, ReproductionResult result, int attempts = 1) =>
        Escalations.Propose(Candidate()) with { Outcome = outcome, Reproduced = result, ReproductionAttempts = attempts };
}
