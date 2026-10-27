using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates.Steps;

public sealed class TestStepTests
{
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { "Shop.Tests.Legacy.Flaky" };

    [Fact]
    public void KnownFailuresThatAccountForEveryCountedFailurePass()
    {
        GateStep step = Judge("failed Shop.Tests.Legacy.Flaky (12ms)\nTest run summary: Failed!\n  total: 10\n  failed: 1\n  succeeded: 9\n");

        Assert.Equal(Verdict.Pass, step.Verdict);
        Assert.Contains("1 known failures (baseline)", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailureTheOutputDoesNotNameCannotHideBehindTheBaseline()
    {
        GateStep step = Judge("failed Shop.Tests.Legacy.Flaky (12ms)\nTest run summary: Failed!\n  total: 10\n  failed: 2\n  succeeded: 8\n");

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("1 failures could not be attributed", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingSummaryFailsRatherThanTrustingTheNamedFailures()
    {
        GateStep step = Judge("failed Shop.Tests.Legacy.Flaky (12ms)\n");

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("summary of failures is missing", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownFailureFails()
    {
        GateStep step = Judge("failed Shop.Tests.New.Broken (3ms)\n  failed: 1\n");

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("Shop.Tests.New.Broken", step.Detail, StringComparison.Ordinal);
    }

    private static GateStep Judge(string output)
    {
        ProcessResult testRun = new(2, output, string.Empty, 0, TimedOut: false);
        return TestStep.JudgeAgainstBaseline(testRun, 10, FailedTests.Find(output), Known);
    }

    [Fact]
    public void VSTestPassesWhenTestsRanAndNoneFailed()
    {
        GateStep step = TestStep.JudgeVSTest(Run(0), reportCount: 1, new TrxSummary(4, 4, 0, []), () => Known);

        Assert.Equal(Verdict.Pass, step.Verdict);
        Assert.Equal("total 4", step.Detail);
    }

    [Fact]
    public void VSTestWithoutAReportIsNotEarnedOnSuccessAndAnErrorOtherwise()
    {
        Assert.Equal(Verdict.NotEarned, TestStep.JudgeVSTest(Run(0), reportCount: 0, new TrxSummary(0, 0, 0, []), () => Known).Verdict);
        Assert.Equal(Verdict.Error, TestStep.JudgeVSTest(Run(1), reportCount: 0, new TrxSummary(0, 0, 0, []), () => Known).Verdict);
    }

    [Fact]
    public void VSTestWithNoExecutedTestIsNotEarnedBecauseAFilterThatMatchesNothingExitsZero()
    {
        Assert.Equal(Verdict.NotEarned, TestStep.JudgeVSTest(Run(0), reportCount: 1, new TrxSummary(0, 0, 0, []), () => Known).Verdict);
    }

    [Fact]
    public void VSTestFailuresAreNamedAndJudgedAgainstTheBaseline()
    {
        GateStep unknown = TestStep.JudgeVSTest(Run(1), reportCount: 1, new TrxSummary(5, 5, 1, ["Shop.Tests.New.Broken"]), () => Known);
        GateStep known = TestStep.JudgeVSTest(Run(1), reportCount: 1, new TrxSummary(5, 5, 1, ["Shop.Tests.Legacy.Flaky"]), () => Known);

        Assert.Equal(Verdict.Fail, unknown.Verdict);
        Assert.Contains("Shop.Tests.New.Broken", unknown.Detail, StringComparison.Ordinal);
        Assert.Equal(Verdict.Pass, known.Verdict);
    }

    [Fact]
    public void AVSTestFailureWithoutAFailedTestIsAnError()
    {
        GateStep step = TestStep.JudgeVSTest(Run(1), reportCount: 1, new TrxSummary(5, 5, 0, []), () => Known);

        Assert.Equal(Verdict.Error, step.Verdict);
        Assert.Contains("no test failed", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ATestingPlatformRunWhereEveryTestWasSkippedIsNotEarnedThoughItExitsZero()
    {
        const string output = "skipped T.A.One (0ms)\nTest run summary: Zero tests ran\n  total: 2\n  failed: 0\n  succeeded: 0\n  skipped: 2\n";

        GateStep step = TestStep.JudgeTestingPlatform(new ProcessResult(0, output, string.Empty, 0, TimedOut: false));

        Assert.Equal(Verdict.NotEarned, step.Verdict);
        Assert.Contains("skipped", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ATestingPlatformRunWithOneModuleSkippedAndAnotherPassingPasses()
    {
        const string output = "Test run summary: Zero tests ran\n  total: 1\n  failed: 0\n  succeeded: 0\n  skipped: 1\n"
            + "Test run summary: Passed!\n  total: 3\n  failed: 0\n  succeeded: 3\n  skipped: 0\n";

        GateStep step = TestStep.JudgeTestingPlatform(new ProcessResult(0, output, string.Empty, 0, TimedOut: false));

        Assert.Equal(Verdict.Pass, step.Verdict);
        Assert.Equal("total 4", step.Detail);
    }

    [Fact]
    public void ATestingPlatformRunWithASummaryWithoutCountsThatSaysZeroTestsRanIsNotEarned()
    {
        GateStep step = TestStep.JudgeTestingPlatform(new ProcessResult(0, "Test run summary: Zero tests ran\n  total: 2\n", string.Empty, 0, TimedOut: false));

        Assert.Equal(Verdict.NotEarned, step.Verdict);
    }

    private static ProcessResult Run(int exitCode) => new(exitCode, string.Empty, string.Empty, 0, TimedOut: false);
}
