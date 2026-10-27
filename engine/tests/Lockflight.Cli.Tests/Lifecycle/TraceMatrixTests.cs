using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Lifecycle;

public sealed class TraceMatrixTests
{
    private static readonly Requirement[] Requirements = [new("FR-001", 1, "first"), new("FR-002", 2, "second")];

    [Fact]
    public void TestsThatStillExistCoverTheirRequirements()
    {
        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, Record(), [Test("First", "FR-001"), Test("Second", "FR-002")]);

        Assert.Equal([["ShippingTests.First"], ["ShippingTests.Second"]], rows.Select(row => row.Tests));
        Assert.All(rows, row => Assert.Empty(row.Lost));
    }

    [Fact]
    public void AnAssertionFreeTestStillCoversButIsMarked()
    {
        RedGreenRecord record = Record();
        record.Cycles[0] = record.Cycles[0] with { Tests = [Green("First", "FR-001") with { AssertionFree = true }, Green("Second", "FR-002")] };

        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, record, [Test("First", "FR-001"), Test("Second", "FR-002")]);

        Assert.Equal([["ShippingTests.First (assertion-free)"], ["ShippingTests.Second"]], rows.Select(row => row.Tests));
        Assert.All(rows, row => Assert.True(row.Covered));
    }

    [Fact]
    public void ATestDeletedOrRenamedAfterGreenNoLongerCovers()
    {
        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, Record(), [Test("FirstRenamed", "FR-001"), Test("Second", "FR-002")]);

        Assert.False(rows[0].Covered);
        Assert.Equal(["ShippingTests.First (deleted or renamed)"], rows[0].Lost);
        Assert.True(rows[1].Covered);
    }

    [Fact]
    public void ATestThatDroppedTheRequirementTraitNoLongerCovers()
    {
        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, Record(), [Test("First"), Test("Second", "FR-002")]);

        Assert.Equal(["ShippingTests.First (no longer names FR-001)"], rows[0].Lost);
    }

    [Fact]
    public void SkippedAndExplicitTestsNoLongerCover()
    {
        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(
            Requirements, Record(), [Test("First", "FR-001") with { SkipReason = "later" }, Test("Second", "FR-002") with { IsExplicit = true }]);

        Assert.Equal(["ShippingTests.First (skipped)"], rows[0].Lost);
        Assert.Equal(["ShippingTests.Second (explicit)"], rows[1].Lost);
        Assert.DoesNotContain(rows, row => row.Covered);
    }

    [Fact]
    public void ARequirementKeepsCoverageWhileOneOfItsTestsRemains()
    {
        RedGreenRecord record = Record();
        record.Cycles.Add(new RedGreenCycle(2, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [Green("FirstAgain", "FR-001")]));

        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, record, [Test("FirstAgain", "FR-001"), Test("Second", "FR-002")]);

        Assert.True(rows[0].Covered);
        Assert.Equal(["ShippingTests.First (deleted or renamed)"], rows[0].Lost);
    }

    [Fact]
    public void AnOpenCycleTestMarksItsRequirementPending()
    {
        RedGreenRecord record = Record();
        record.Cycles.Add(new RedGreenCycle(2, DateTimeOffset.UnixEpoch, GreenAt: null, [Green("Third", "FR-002")]));

        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(Requirements, record, [Test("First", "FR-001"), Test("Third", "FR-002")]);

        Assert.True(rows[1].Pending);
        Assert.False(rows[0].Pending);
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
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [Green("First", "FR-001"), Green("Second", "FR-002")]));
        return record;
    }

    private static RedGreenTest Green(string method, string requirement) =>
        new($"Shop.Tests:Shop.Tests.ShippingTests.{method}", $"Shop.Tests.ShippingTests.{method}", RedClass.RedStub, [requirement]);

    private static TestCase Test(string method, params string[] requirements) => new("Shop.Tests", "Shop.Tests.ShippingTests", method, requirements, null);
}
