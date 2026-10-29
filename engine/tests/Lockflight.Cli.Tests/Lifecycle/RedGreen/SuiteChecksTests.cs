using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class SuiteChecksTests
{
    private static readonly IReadOnlySet<string> NoKnownFailures = new HashSet<string>(StringComparer.Ordinal);

    [Fact]
    public void AGreenWithEveryTestRunningHasNoProblems()
    {
        RedGreenRecord record = Record(explicitAtBaseline: []);
        RedGreenCycle open = OpenCycle(record);

        List<string> problems = SuiteChecks.GreenProblems(record, open, [Test("Old"), Test("Earlier"), Test("New")], [Passed("Old"), Passed("Earlier"), Passed("New")], NoKnownFailures);

        Assert.Empty(problems);
    }

    [Fact]
    public void ABaselineTestMarkedExplicitSinceTheBaselineFailsTheGreen()
    {
        RedGreenRecord record = Record(explicitAtBaseline: []);
        RedGreenCycle open = OpenCycle(record);

        List<string> problems = SuiteChecks.GreenProblems(
            record, open, [Test("Old", isExplicit: true), Test("Earlier"), Test("New")], [NotRun("Old"), Passed("Earlier"), Passed("New")], NoKnownFailures);

        Assert.Equal("tests newly marked Explicit (a normal run never executes them): T:A.Old", Assert.Single(problems));
    }

    [Fact]
    public void AnEarlierCycleTestMarkedExplicitFailsTheGreen()
    {
        RedGreenRecord record = Record(explicitAtBaseline: []);
        RedGreenCycle open = OpenCycle(record);

        List<string> problems = SuiteChecks.GreenProblems(
            record, open, [Test("Old"), Test("Earlier", isExplicit: true), Test("New")], [Passed("Old"), NotRun("Earlier"), Passed("New")], NoKnownFailures);

        Assert.Contains("T:A.Earlier", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void ATestAlreadyExplicitAtTheBaselineDoesNotFailTheGreen()
    {
        RedGreenRecord record = Record(explicitAtBaseline: ["T:A.Old"]);
        RedGreenCycle open = OpenCycle(record);

        List<string> problems = SuiteChecks.GreenProblems(
            record, open, [Test("Old", isExplicit: true), Test("Earlier"), Test("New")], [NotRun("Old"), Passed("Earlier"), Passed("New")], NoKnownFailures);

        Assert.Empty(problems);
    }

    [Fact]
    public void ACycleTestMarkedExplicitIsBothNotPassingAndNewlyExplicit()
    {
        RedGreenRecord record = Record(explicitAtBaseline: []);
        RedGreenCycle open = OpenCycle(record);

        List<string> problems = SuiteChecks.GreenProblems(
            record, open, [Test("Old"), Test("Earlier"), Test("New", isExplicit: true)], [Passed("Old"), Passed("Earlier"), NotRun("New")], NoKnownFailures);

        Assert.Equal(2, problems.Count);
        Assert.StartsWith("cycle tests not passing", problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ARuntimeSkipSinceTheBaselineFailsTheGreen()
    {
        RedGreenRecord record = Record(explicitAtBaseline: []);
        RedGreenCycle open = OpenCycle(record);

        List<string> problems = SuiteChecks.GreenProblems(
            record, open, [Test("Old"), Test("Earlier"), Test("New")], [Skipped("Old"), Passed("Earlier"), Passed("New")], NoKnownFailures);

        Assert.Equal("new skipped tests: T:A.Old", Assert.Single(problems));
    }

    [Fact]
    public void KnownTestsSwitchedOffSinceTheBaselineAreReportedBeforeTheNextRed()
    {
        RedGreenRecord record = Record(explicitAtBaseline: []);

        List<string> problems = SuiteChecks.WeakenedKnownTests(record, [Test("Old", skipReason: "later"), Test("Earlier", isExplicit: true), Test("Brand", isExplicit: true)]);

        Assert.Equal(["known tests newly skipped: T:A.Old", "known tests newly marked Explicit: T:A.Earlier"], problems);
    }

    [Fact]
    public void KnownTestsAsTheyWereAtTheBaselineAreNotWeakened()
    {
        RedGreenRecord record = Record(explicitAtBaseline: ["T:A.Old"]);

        Assert.Empty(SuiteChecks.WeakenedKnownTests(record, [Test("Old", isExplicit: true), Test("Earlier")]));
    }

    // Baseline test Old and a closed cycle with Earlier; OpenCycle adds New.
    [Fact]
    public void AnExplicitTestReportedAsNotExecutedIsNoNewSkip()
    {
        RedGreenRecord record = Record(explicitAtBaseline: ["T:A.Old"]);
        RedGreenCycle open = OpenCycle(record);

        List<string> problems = SuiteChecks.GreenProblems(
            record, open, [Test("Old", isExplicit: true), Test("Earlier"), Test("New")], [Skipped("Old"), Passed("Earlier"), Passed("New")], NoKnownFailures);

        Assert.Empty(problems);
    }

    [Fact]
    public void RowsSkippedAtRunTimeBeforeAnyChangeAreNoNewSkipAtAGreen()
    {
        RedGreenRecord withoutRun = Record(explicitAtBaseline: []);
        RedGreenRecord started = Record(explicitAtBaseline: []);
        RedGreenRecord record = started with { BaselineSkippedRows = SuiteChecks.SkippedRows([Skipped("Old"), Passed("Old"), Passed("Earlier")]) };
        TestCase[] inventory = [Test("Old"), Test("Earlier"), Test("New")];
        ExecutedTest[] results = [Skipped("Old"), Passed("Old"), Passed("Earlier"), Passed("New")];

        Assert.Empty(SuiteChecks.GreenProblems(record, OpenCycle(record), inventory, results, NoKnownFailures));
        Assert.Equal("new skipped tests: T:A.Old", Assert.Single(SuiteChecks.GreenProblems(withoutRun, OpenCycle(withoutRun), inventory, results, NoKnownFailures)));
    }

    [Fact]
    public void MoreRowsSkippedThanAtTheBaselineAreANewSkip()
    {
        RedGreenRecord started = Record(explicitAtBaseline: []);
        RedGreenRecord record = started with { BaselineSkippedRows = SuiteChecks.SkippedRows([Skipped("Old"), Passed("Old")]) };

        List<string> problems = SuiteChecks.GreenProblems(
            record, OpenCycle(record), [Test("Old"), Test("Earlier"), Test("New")], [Skipped("Old"), Skipped("Old"), Passed("Earlier"), Passed("New")], NoKnownFailures);

        Assert.Equal("new skipped tests: T:A.Old", Assert.Single(problems));
    }

    [Fact]
    public void AKnownTestLeftOutByTheStopFilterSinceTheBaselineIsRefusedButOneLeftOutThenIsNot()
    {
        RedGreenRecord record = Record(explicitAtBaseline: []) with { BaselineRegressionFilter = new(["Category!=Slow"], 0) { ExcludedTests = [] } };
        OpenCycle(record);
        HashSet<string> excluded = new(StringComparer.Ordinal) { "T:A.Old", "T:A.New" };

        Assert.Equal(["T:A.Old"], SuiteChecks.NewlyLeftOut(record, excluded));

        RedGreenRecord leftOutThen = record with { BaselineRegressionFilter = new(["Category!=Slow"], 1) { ExcludedTests = ["T:A.Old"] } };
        Assert.Empty(SuiteChecks.NewlyLeftOut(leftOutThen, excluded));
    }

    private static RedGreenRecord Record(string[] explicitAtBaseline)
    {
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string>(StringComparer.Ordinal) { "T:A.Old" },
            BaselineExplicit = explicitAtBaseline.ToHashSet(StringComparer.Ordinal),
            TestProjects = ["tests"],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [new RedGreenTest("T:A.Earlier", "A.Earlier", RedClass.RedStub, [])]));
        return record;
    }

    private static RedGreenCycle OpenCycle(RedGreenRecord record)
    {
        RedGreenCycle open = new(2, DateTimeOffset.UnixEpoch, GreenAt: null, [new RedGreenTest("T:A.New", "A.New", RedClass.RedAssert, [])]);
        record.Cycles.Add(open);
        return open;
    }

    private static TestCase Test(string method, string? skipReason = null, bool isExplicit = false) => new("T", "A", method, [], skipReason, isExplicit);

    private static ExecutedTest Passed(string method) => new($"T:A.{method}", $"A.{method}", TestOutcome.Pass, null, null);

    private static ExecutedTest NotRun(string method) => new($"T:A.{method}", $"A.{method}", TestOutcome.NotRun, null, null);

    private static ExecutedTest Skipped(string method) => new($"T:A.{method}", $"A.{method}", TestOutcome.Skip, null, null);
}
