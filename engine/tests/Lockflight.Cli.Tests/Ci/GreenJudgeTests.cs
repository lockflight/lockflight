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
    public void ACycleTestDeletedOrRenamedAfterItsRedIsNotEarned()
    {
        GreenJudgement green = Decide([BaseTest], [Pass(BaseTest)]);

        Assert.Equal(CheckVerdict.NotEarned, green.Verdict);
        Assert.Contains("cycle tests missing at head", green.Why, StringComparison.Ordinal);
        Assert.Contains("keep the name it went red with", green.Why, StringComparison.Ordinal);
        Assert.Equal("missing", green.AtHead[CycleTest.Key]);
    }

    [Fact]
    public void ABaseTestDeletedAtHeadStillFails()
    {
        GreenJudgement green = Decide([CycleTest], [Pass(CycleTest)]);

        Assert.Equal(CheckVerdict.Fail, green.Verdict);
        Assert.Contains("tests deleted", green.Why, StringComparison.Ordinal);
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
        GreenJudgement green = Decide([BaseTest, CycleTest], [Skip(BaseTest), Pass(CycleTest)], baseSkips: new BaseSkipCheck(new Dictionary<string, int> { [BaseTest.Key] = 1 }, [], null));

        Assert.Equal(CheckVerdict.Pass, green.Verdict);
    }

    [Fact]
    public void ARunTimeSkipMatchesTheBaseOnlyWithTheSameMessage()
    {
        ExecutedTest[] head = [Skip(BaseTest) with { SkipReason = "no Docker here" }, Skip(BaseTest) with { SkipReason = "later" }];
        ExecutedTest[] onBase = [Skip(BaseTest) with { SkipReason = " no Docker here " }, Pass(BaseTest)];

        IReadOnlyDictionary<string, int> matched = GreenJudge.MatchedSkips(head, onBase);

        Assert.Equal(1, matched[BaseTest.Key]);
        GreenJudgement green = Decide([BaseTest, CycleTest], [.. head, Pass(CycleTest)], baseSkips: new BaseSkipCheck(matched, [], null));
        Assert.Equal(CheckVerdict.Fail, green.Verdict);
        Assert.Contains("new skipped tests", green.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void ATestMissingFromTheBaseReportOrSkippedWithoutAMessageIsNotCompared()
    {
        TestCase other = Test("Other");
        ExecutedTest[] head = [Skip(BaseTest) with { SkipReason = "no Docker here" }, Skip(other)];
        ExecutedTest[] onBase = [Skip(other)];

        BaseSkipCheck check = GreenJudge.CompareSkips(head, onBase, [BaseTest.Key, other.Key]);

        Assert.Equal([BaseTest.Key, other.Key], check.Unchecked);
        Assert.Empty(check.MatchedRows);
        Assert.Equal("not in the base run's report", check.Failure);
    }

    [Fact]
    public void ABaseThatCouldNotRunTheRunTimeSkipsLeavesGreenNotEarned()
    {
        GreenJudgement green = Decide([BaseTest, CycleTest], [Skip(BaseTest), Pass(CycleTest)], baseSkips: new BaseSkipCheck(new Dictionary<string, int>(), [BaseTest.Key], "the runner did not finish on the base (exit 3)"));

        Assert.Equal(CheckVerdict.NotEarned, green.Verdict);
        Assert.Contains("could not be run on the base", green.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherFailureStillFailsWhenTheBaseCouldNotRunTheRunTimeSkips()
    {
        GreenJudgement green = Decide([BaseTest, CycleTest], [Skip(BaseTest), Fail(CycleTest)], baseSkips: new BaseSkipCheck(new Dictionary<string, int>(), [BaseTest.Key], "out of time"));

        Assert.Equal(CheckVerdict.Fail, green.Verdict);
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

        Assert.Equal(CheckVerdict.Fail, GreenJudge.Decide(new HeadRun(broken, null, new HashSet<string>(), null, new HashSet<string>()), [BaseTest], BaseSkipCheck.None, []).Verdict);
    }

    private static GreenJudgement Decide(
        IReadOnlyList<TestCase> inventory,
        IReadOnlyList<ExecutedTest> results,
        string[]? passedOnRetry = null,
        string[]? knownFailing = null,
        BaseSkipCheck? baseSkips = null)
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
        return GreenJudge.Decide(head, [BaseTest], baseSkips ?? BaseSkipCheck.None, [cycle]);
    }

    private static TestWorkspace Workspace() => new("/repo", [], TimeSpan.FromMinutes(1), TimeProvider.System);

    private static TestCase Test(string method) => new("Shop.Tests", "Shop.Tests.PriceTests", method, [], SkipReason: null);

    private static ExecutedTest Pass(TestCase test) => new(test.Key, test.MethodFilter, TestOutcome.Pass, null, null);

    private static ExecutedTest Fail(TestCase test) => new(test.Key, test.MethodFilter, TestOutcome.Fail, "Xunit.Sdk.EqualException", null);

    private static ExecutedTest Skip(TestCase test) => new(test.Key, test.MethodFilter, TestOutcome.Skip, null, null);
}
