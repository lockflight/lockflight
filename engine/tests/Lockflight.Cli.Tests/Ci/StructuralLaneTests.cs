using Lockflight.Cli.ApprovalsOfRecord;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;
using Requirement = Lockflight.Cli.Lifecycle.Requirement;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// Whether a pull request whose spec is all <c>[structural]</c> needs no red commit is decided from git alone: every condition is
/// met, or the unmet one is named.
/// </summary>
public sealed class StructuralLaneTests
{
    private const string SpecPath = "specs/012-pricing/spec.md";
    private const string SpecGlob = "specs/**";

    [Fact]
    public void ASpecChangedHereUnderCodeownersIsEligible()
    {
        StructuralEligibility eligibility = Decide(Facts());

        Assert.True(eligibility.Eligible);
        Assert.Equal(StructuralRoute.Codeowners, eligibility.Route);
        Assert.Equal(["FR-010", "FR-011"], eligibility.Requirements);
        Assert.Equal(SpecPath, eligibility.SpecPath);
    }

    [Fact]
    public void ASpecMergedFirstWhereSpecFirstAppliesIsEligible()
    {
        StructuralEligibility eligibility = Decide(Facts() with
        {
            ChangedFiles = ["src/Shop/Price.cs"],
            Approvals = Approvals(Coverage(covers: false), new SpecFirstFacts(Applies: true, ["approvals.default=spec-first"], SpecPath, SpecOnBase: true, [])),
        });

        Assert.True(eligibility.Eligible);
        Assert.Equal(StructuralRoute.SpecFirst, eligibility.Route);
    }

    [Fact]
    public void AMixedSpecIsNoStructuralChange() =>
        Assert.Null(StructuralLane.Decide(Facts() with { Requirements = [Structural("FR-010"), new Requirement("FR-011", 2, "Totals MUST include tax")] }));

    [Fact]
    public void NoSpecIsNoStructuralChange() =>
        Assert.Null(StructuralLane.Decide(Facts() with { Spec = SpecResolution.None(null), Requirements = [] }));

    [Fact]
    public void ASpecTheLookupCannotVouchForIsNoStructuralChange() =>
        Assert.Null(StructuralLane.Decide(Facts() with { Spec = Spec() with { Problem = "the branch and the tests disagree on the feature" } }));

    [Fact]
    public void AnInterimClassificationIsNotEligible() =>
        Assert.Equal(StructuralCondition.Routed, Assert.Single(Decide(Facts() with { Routed = false }).Unmet).Condition);

    [Fact]
    public void AUnitOfAWorkspaceShellIsNotEligible()
    {
        StructuralUnmet unmet = Assert.Single(Decide(Facts() with { Unit = true }).Unmet);

        Assert.Equal(StructuralCondition.Routed, unmet.Condition);
        Assert.Contains("workspace shell", unmet.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void APushIsNotEligible() =>
        Assert.Equal(StructuralCondition.Routed, Assert.Single(Decide(Facts() with { PullRequest = false }).Unmet).Condition);

    [Fact]
    public void ARedCommitOrAMalformedTrailerKeepsRedGreen() =>
        Assert.Equal(StructuralCondition.NoRedCommit, Assert.Single(Decide(Facts() with { RedCommits = 1 }).Unmet).Condition);

    [Fact]
    public void ASpecNotChangedHereWithoutSpecFirstIsNotReviewed()
    {
        StructuralEligibility eligibility = Decide(Facts() with { ChangedFiles = ["src/Shop/Price.cs"] });

        StructuralUnmet unmet = Assert.Single(eligibility.Unmet);
        Assert.Equal(StructuralCondition.SpecReviewed, unmet.Condition);
        Assert.Contains("is not changed in this pull request and spec-first does not apply", unmet.Why, StringComparison.Ordinal);
        Assert.Null(eligibility.Route);
    }

    [Fact]
    public void ASpecPathCodeownersDoesNotCoverIsNotReviewed()
    {
        StructuralUnmet unmet = Assert.Single(Decide(Facts() with { Approvals = Approvals(Coverage(covers: false), SpecFirstFacts.NotApplicable) }).Unmet);

        Assert.Equal(StructuralCondition.SpecReviewed, unmet.Condition);
        Assert.Contains("gives specs/012-pricing/spec.md no owner", unmet.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void APlatformWithoutCodeownersNeedsSpecFirst()
    {
        CodeownersCoverage none = new(File: null, "base", [], [SpecGlob], GitError: null);

        StructuralUnmet unmet = Assert.Single(Decide(Facts() with { Approvals = Approvals(none, SpecFirstFacts.NotApplicable) }).Unmet);

        Assert.Equal(StructuralCondition.SpecReviewed, unmet.Condition);
        Assert.Contains("no CODEOWNERS file", unmet.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void ASpecChangedWhereSpecFirstAppliesIsNotReviewedBySpecFirst()
    {
        SpecFirstFacts changed = new(Applies: true, ["approvals.default=spec-first"], SpecPath, SpecOnBase: true, [SpecPath]);

        StructuralUnmet unmet = Assert.Single(Decide(Facts() with { Approvals = Approvals(Coverage(covers: false), changed) }).Unmet);

        Assert.Contains("spec-first applies", unmet.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovalFactsThatCouldNotBeReadAreNotAReview() =>
        Assert.Equal(StructuralCondition.SpecReviewed, Assert.Single(Decide(Facts() with { Approvals = null }).Unmet).Condition);

    [Theory]
    [InlineData(SignalIds.ConfigChanged)]
    [InlineData(SignalIds.DepsChanged)]
    [InlineData(SignalIds.DbMigration)]
    [InlineData(SignalIds.DbSql)]
    [InlineData(SignalIds.DbModel)]
    [InlineData(SignalIds.ApiContractFile)]
    [InlineData(SignalIds.ApiPublicApiFile)]
    [InlineData(SignalIds.BinaryChanged)]
    [InlineData(SignalIds.CriticalPolicy)]
    [InlineData(SignalIds.CiWorkflowChanged)]
    public void ASignalThatCarriesBehaviourIsNotEligible(string signal)
    {
        StructuralUnmet unmet = Assert.Single(Decide(Facts() with { Signals = [Signal(signal)] }).Unmet);

        Assert.Equal(StructuralCondition.NoBehaviourSignal, unmet.Condition);
        Assert.Contains($"{signal} in appsettings.json", unmet.Why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SignalIds.ApiPublicMember)]
    [InlineData(SignalIds.SizeLines)]
    [InlineData(SignalIds.TestsChanged)]
    public void ASignalOfTheCodeItselfLeavesItEligible(string signal) =>
        Assert.True(Decide(Facts() with { Signals = [Signal(signal)] }).Eligible);

    [Fact]
    public void EveryUnmetConditionIsListed()
    {
        StructuralEligibility eligibility = Decide(Facts() with { Routed = false, RedCommits = 2, ChangedFiles = [], Signals = [Signal(SignalIds.DepsChanged)] });

        Assert.Equal([StructuralCondition.Routed, StructuralCondition.NoRedCommit, StructuralCondition.SpecReviewed, StructuralCondition.NoBehaviourSignal],
            eligibility.Unmet.Select(unmet => unmet.Condition));
        Assert.False(eligibility.Eligible);
    }

    private static StructuralEligibility Decide(StructuralLaneFacts facts) =>
        StructuralLane.Decide(facts) ?? throw new InvalidOperationException("the spec is all structural, so the lane is decided");

    private static StructuralLaneFacts Facts() => new(
        PullRequest: true,
        Routed: true,
        Unit: false,
        RedCommits: 0,
        Spec(),
        [Structural("FR-010"), Structural("FR-011")],
        Approvals(Coverage(covers: true), SpecFirstFacts.NotApplicable),
        [SpecGlob],
        ["src/Shop/Price.cs", SpecPath],
        []);

    private static SpecResolution Spec() => new("012-pricing", "branch", SpecPath, "# Spec\n", null, SpecOrigin.Head, Problem: null);

    private static Requirement Structural(string id) => new(id, 1, "Totals MUST stay as they are") { Marker = "structural", Structural = true };

    private static CodeownersCoverage Coverage(bool covers) =>
        covers ? new(".github/CODEOWNERS", "base", [SpecGlob], [], GitError: null) : new(".github/CODEOWNERS", "base", [], [$"{SpecGlob} ({SpecPath} has no owner)"], GitError: null);

    private static ApprovalsFacts Approvals(CodeownersCoverage coverage, SpecFirstFacts specFirst) =>
        new(ApprovalCheckMode.Report, ApprovalMode.SamePr, coverage, specFirst, []);

    private static ChangeSignal Signal(string id) => new(id, ChangeClass.Feature, SignalStrength.Heuristic, "appsettings.json", "detail");
}
