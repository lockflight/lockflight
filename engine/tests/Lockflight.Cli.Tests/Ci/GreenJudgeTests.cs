using Lockflight.Cli.Ci;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Ci;

public sealed class GreenJudgeTests
{
    private static readonly TestCase BaseTest = Test("ABaseTest");
    private static readonly TestCase CycleTest = Test("ShippingCost");

    [Fact]
    public void EveryTestPassingWithTheCycleTestPresentIsGreen()
    {
        GreenJudgement green = Decide([BaseTest, CycleTest], [Pass(BaseTest), Pass(CycleTest)]);

        Assert.Equal(CheckVerdict.Pass, green.Verdict);
        Assert.Equal("pass", green.AtHead[CycleTest.Key]);
    }

    [Fact]
    public void ACycleTestDeletedAfterItsRedFails()
    {
        GreenJudgement green = Decide([BaseTest], [Pass(BaseTest)]);

        Assert.Equal(CheckVerdict.Fail, green.Verdict);
        Assert.Contains("tests deleted", green.Why, StringComparison.Ordinal);
        Assert.Equal("missing", green.AtHead[CycleTest.Key]);
    }

    [Fact]
    public void ABaseTestSkippedAtHeadFails()
    {
        GreenJudgement green = Decide([BaseTest with { SkipReason = "later" }, CycleTest], [Skip(BaseTest), Pass(CycleTest)]);

        Assert.Equal(CheckVerdict.Fail, green.Verdict);
        Assert.Contains("new skipped tests", green.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void ABaseTestSkippedAtRunTimeOnTheBaseTooIsNoNewSkip()
    {
        GreenJudgement green = Decide([BaseTest, CycleTest], [Skip(BaseTest), Pass(CycleTest)], baseSkippedRows: new Dictionary<string, int> { [BaseTest.Key] = 1 });

        Assert.Equal(CheckVerdict.Pass, green.Verdict);
    }

    [Fact]
    public void ABaseTestSkippedAtRunTimeOnlyAtHeadFails()
    {
        GreenJudgement green = Decide([BaseTest, CycleTest], [Skip(BaseTest), Pass(CycleTest)]);

        Assert.Equal(CheckVerdict.Fail, green.Verdict);
        Assert.Contains("new skipped tests", green.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheBaseTestsSkippedAtRunTimeAtHeadAreRunAgainOnTheBase()
    {
        TestCase attributeSkipped = Test("Later") with { SkipReason = "later" };
        HeadRun head = new(new TreeListing("h", TreeOutcome.Ok, Workspace(), [BaseTest, CycleTest, attributeSkipped], null), [Skip(BaseTest), Skip(CycleTest), Skip(attributeSkipped)], new HashSet<string>(), null, new HashSet<string>());

        Assert.Equal([BaseTest.Key], GreenJudge.SkippedAtRunTime(head, [BaseTest, attributeSkipped]).Select(test => test.Key));
    }

    [Fact]
    public void AFailureOnTheBasesKnownFailingListDoesNotFail()
    {
        GreenJudgement green = Decide([BaseTest, CycleTest], [Fail(BaseTest), Pass(CycleTest)], knownFailing: [BaseTest.MethodFilter]);

        Assert.Equal(CheckVerdict.Pass, green.Verdict);
    }

    [Fact]
    public void ATestMissingFromTheReportFails()
    {
        GreenJudgement green = Decide([BaseTest, CycleTest], [Pass(CycleTest)]);

        Assert.Equal(CheckVerdict.Fail, green.Verdict);
        Assert.Contains("missing from the run's report", green.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void ACycleTestThatPassedOnlyOnARetryIsNotEarned()
    {
        GreenJudgement green = Decide([BaseTest, CycleTest], [Pass(BaseTest), Pass(CycleTest)], passedOnRetry: [CycleTest.Key]);

        Assert.Equal(CheckVerdict.NotEarned, green.Verdict);
        Assert.Equal("flaky", green.AtHead[CycleTest.Key]);
    }

    [Fact]
    public void AnotherTestThatPassedOnlyOnARetryIsAWarning()
    {
        GreenJudgement green = Decide([BaseTest, CycleTest], [Pass(BaseTest), Pass(CycleTest)], passedOnRetry: [BaseTest.Key]);

        Assert.Equal(CheckVerdict.Pass, green.Verdict);
        Assert.Contains("passed only when run again", green.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void ATestNewAtHeadWithoutARedIsListed()
    {
        TestCase regression = Test("ARegression");

        GreenJudgement green = Decide([BaseTest, CycleTest, regression], [Pass(BaseTest), Pass(CycleTest), Pass(regression)]);

        Assert.Equal([regression.Key], green.WithoutRed);
    }

    [Fact]
    public void AHeadThatDoesNotBuildFails()
    {
        TreeListing broken = new("h", TreeOutcome.CompileFailed, Workspace(), null, "error CS1002");

        Assert.Equal(CheckVerdict.Fail, GreenJudge.Decide(new HeadRun(broken, null, new HashSet<string>(), null, new HashSet<string>()), [BaseTest], new Dictionary<string, int>(), []).Verdict);
    }

    private static GreenJudgement Decide(
        IReadOnlyList<TestCase> inventory,
        IReadOnlyList<ExecutedTest> results,
        string[]? passedOnRetry = null,
        string[]? knownFailing = null,
        IReadOnlyDictionary<string, int>? baseSkippedRows = null)
    {
        TreeListing listing = new("h", TreeOutcome.Ok, Workspace(), inventory, null);
        HeadRun head = new(listing, results, new HashSet<string>(passedOnRetry ?? []), null, new HashSet<string>(knownFailing ?? []));
        CycleJudgement cycle = new(
            new RedCommit("r", "test: red", "t", "1", Malformed: null, Superseded: false),
            TreeOutcome.Ok,
            [new RedGreenTest(CycleTest.Key, CycleTest.MethodFilter, RedClass.RedStub, [])],
            new Dictionary<string, RedVerdict>(),
            CheckVerdict.Pass,
            "accepted");
        return GreenJudge.Decide(head, [BaseTest], baseSkippedRows ?? new Dictionary<string, int>(), [cycle]);
    }

    private static TestWorkspace Workspace() => new("/repo", [], TimeSpan.FromMinutes(1), TimeProvider.System);

    private static TestCase Test(string method) => new("Shop.Tests", "Shop.Tests.PriceTests", method, [], SkipReason: null);

    private static ExecutedTest Pass(TestCase test) => new(test.Key, test.MethodFilter, TestOutcome.Pass, null, null);

    private static ExecutedTest Fail(TestCase test) => new(test.Key, test.MethodFilter, TestOutcome.Fail, "Xunit.Sdk.EqualException", null);

    private static ExecutedTest Skip(TestCase test) => new(test.Key, test.MethodFilter, TestOutcome.Skip, null, null);
}
