using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Lifecycle;

public sealed class StructuralTests
{
    private static readonly Requirement[] Requirements = [new("FR-001", 1, "first"), new("FR-007", 2, "layers") { Structural = true, Marker = "structural" }];

    [Fact]
    public void AStructuralMarkerIsReadAndLeftOutOfTheText()
    {
        SpecDocument spec = SpecDocument.Parse("- **FR-007** [structural]: The domain layer MUST NOT reference the web layer\n- **FR-008**: Orders MUST total\n");

        Assert.True(spec.Requirements[0].Structural);
        Assert.Equal("structural", spec.Requirements[0].Marker);
        Assert.Equal("The domain layer MUST NOT reference the web layer", spec.Requirements[0].Text);
        Assert.False(spec.Requirements[1].Structural);
        Assert.Empty(spec.Placeholders);
    }

    [Fact]
    public void ATestIsStructuralWhenEveryRequirementItNamesIsStructural()
    {
        HashSet<string> structural = ["FR-007", "FR-009"];

        Assert.True(StructuralCheck.IsStructural(["FR-007"], structural));
        Assert.True(StructuralCheck.IsStructural(["FR-007", "FR-009"], structural));
        Assert.False(StructuralCheck.IsStructural(["FR-007", "FR-001"], structural));
        Assert.False(StructuralCheck.IsStructural([], structural));
    }

    [Theory]
    [InlineData(true, nameof(RedClass.GreenBefore), nameof(StructuralOutcome.Preserved))]
    [InlineData(true, nameof(RedClass.RedNoProduction), nameof(StructuralOutcome.ArchitectureRule))]
    [InlineData(true, nameof(RedClass.RedAssert), nameof(StructuralOutcome.BehaviourChanged))]
    [InlineData(true, nameof(RedClass.RedStub), nameof(StructuralOutcome.BehaviourChanged))]
    [InlineData(true, nameof(RedClass.RedOther), nameof(StructuralOutcome.BehaviourChanged))]
    [InlineData(true, nameof(RedClass.Flaky), nameof(StructuralOutcome.BehaviourChanged))]
    [InlineData(false, nameof(RedClass.GreenBefore), nameof(StructuralOutcome.FailsAtHead))]
    public void AStructuralTestCountsWhenItPassesOnTheBaseAndAtHeadOrIsAnArchitectureRule(bool passesAtHead, string onBase, string expected)
    {
        Assert.Equal(Enum.Parse<StructuralOutcome>(expected), StructuralCheck.Classify(passesAtHead, Enum.Parse<RedClass>(onBase), everyRowPassedOnBase: onBase == nameof(RedClass.GreenBefore)));
    }

    [Fact]
    public void ATestThatCouldNotRunOnTheBaseIsUncheckable() =>
        Assert.Equal(StructuralOutcome.Uncheckable, StructuralCheck.Classify(passesAtHead: true, onBase: null, everyRowPassedOnBase: false));

    [Fact]
    public void ATheoryWithOneRowFailingOnTheBaseIsABehaviourChange() =>
        Assert.Equal(StructuralOutcome.BehaviourChanged, StructuralCheck.Classify(passesAtHead: true, RedClass.GreenBefore, everyRowPassedOnBase: false));

    [Fact]
    public void ABehaviourChangeKeepsTheRequirementUncoveredWhateverAnotherStructuralTestShows()
    {
        StructuralTest preserved = new("Shop.Tests:Shop.Tests.LayerTests.Trivial", "Shop.Tests.LayerTests.Trivial", ["FR-007"], StructuralOutcome.Preserved, "passes on the base's code");
        StructuralTest changed = new("Shop.Tests:Shop.Tests.LayerTests.Totals", "Shop.Tests.LayerTests.Totals", ["FR-007"], StructuralOutcome.BehaviourChanged, "RED_ASSERT on the base's code");

        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, Record(), [Test("First", "FR-001")], new Dictionary<string, IReadOnlyList<OutsideFile>>(), [preserved, changed]);

        Assert.False(rows[1].Covered);
        Assert.Contains("behaviour changed — not structural", rows[1].Note, StringComparison.Ordinal);
    }

    [Fact]
    public void AStructuralTestThatPassesOnTheBaseAndAtHeadCoversItsRequirement()
    {
        StructuralTest test = new("Shop.Tests:Shop.Tests.LayerTests.DomainIsClean", "Shop.Tests.LayerTests.DomainIsClean", ["FR-007"], StructuralOutcome.Preserved, "passes on the base's code and at head");

        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, Record(), [Test("First", "FR-001")], new Dictionary<string, IReadOnlyList<OutsideFile>>(), [test]);

        Assert.True(rows[1].Covered);
        Assert.True(rows[1].Structural);
        Assert.Equal(["LayerTests.DomainIsClean (structural)"], rows[1].Tests);
        Assert.Equal(TraceOutcome.Pass, TraceTally.Of(rows).Outcome);
    }

    [Fact]
    public void AStructuralTestThatFailsOnTheBaseIsABehaviourChangeAndCoversNothing()
    {
        StructuralTest test = new("Shop.Tests:Shop.Tests.LayerTests.Totals", "Shop.Tests.LayerTests.Totals", ["FR-007"], StructuralOutcome.BehaviourChanged, "RED_ASSERT on the base's code");

        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, Record(), [Test("First", "FR-001")], new Dictionary<string, IReadOnlyList<OutsideFile>>(), [test]);

        Assert.False(rows[1].Covered);
        Assert.Equal("LayerTests.Totals: behaviour changed — not structural (RED_ASSERT on the base's code)", rows[1].Note);
        Assert.Equal(["FR-007"], TraceTally.Of(rows).Uncovered);
    }

    [Fact]
    public void AnArchitectureRuleTheChangeEstablishesCoversItsRequirementAndSaysSo()
    {
        StructuralTest test = new("Shop.Tests:Shop.Tests.LayerTests.NoCycles", "Shop.Tests.LayerTests.NoCycles", ["FR-007"], StructuralOutcome.ArchitectureRule, "fails on an assertion on the base's code");

        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, Record(), [Test("First", "FR-001")], new Dictionary<string, IReadOnlyList<OutsideFile>>(), [test]);

        Assert.Equal(["LayerTests.NoCycles (structural: architecture rule the change establishes)"], rows[1].Tests);
    }

    private static RedGreenRecord Record()
    {
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string>(StringComparer.Ordinal),
            TestProjects = ["tests"],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [new RedGreenTest("Shop.Tests:Shop.Tests.PriceTests.First", "Shop.Tests.PriceTests.First", RedClass.RedStub, ["FR-001"])]));
        return record;
    }

    private static TestCase Test(string method, params string[] requirements) => new("Shop.Tests", "Shop.Tests.PriceTests", method, requirements, null);
}
