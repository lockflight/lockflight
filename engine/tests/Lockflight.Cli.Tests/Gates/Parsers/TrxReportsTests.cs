using Lockflight.Cli.Gates.Parsers;

namespace Lockflight.Cli.Tests.Gates.Parsers;

public sealed class TrxReportsTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-trx-");

    [Fact]
    public void CountersAreSummedAndEveryFailedResultIsNamed()
    {
        string first = Write("a.trx", Trx(total: 6, executed: 6, failed: 2,
            Result("App.Tests.CalcTests.AddsTwoNumbers", "Failed"),
            Result("App.Tests.CalcTests.Theory(x: 1)", "Failed"),
            Result("App.Tests.CalcTests.Passes", "Passed")));
        string second = Write("b.trx", Trx(total: 3, executed: 2, failed: 0, Result("Other.Tests.T.Ok", "Passed")));

        TrxSummary summary = TrxReports.Read([first, second]);

        Assert.Equal((9, 8, 2), (summary.Total, summary.Executed, summary.Failed));
        Assert.Equal(["App.Tests.CalcTests.AddsTwoNumbers", "App.Tests.CalcTests.Theory(x: 1)"], summary.FailedTests);
        Assert.Empty(summary.Incomplete);
        Assert.Empty(summary.AbnormalTests);
    }

    [Fact]
    public void AReportOfARunWhoseTestHostCrashedIsIncompleteWithTheReason()
    {
        // A crashed test host: the run "failed", no result survives, and the reason is only in the run's messages.
        string crashed = Write("crashed.trx", Trx("Failed", 0, 0, 0,
            RunInfo("[xUnit.net 00:00:00.09]     W.B.Earlier [FAIL]")
            + RunInfo("The active test run was aborted. Reason: Test host process crashed : Process terminated.\nmore detail")));
        string completed = Write("completed.trx", Trx(total: 1, executed: 1, failed: 1, Result("App.Tests.Known", "Failed")));

        TrxSummary summary = TrxReports.Read([completed, crashed]);

        string incomplete = Assert.Single(summary.Incomplete);
        Assert.Equal("crashed.trx: The active test run was aborted. Reason: Test host process crashed : Process terminated.", incomplete);
        Assert.Equal(["App.Tests.Known"], summary.FailedTests);
    }

    [Theory]
    [InlineData("Aborted")]
    [InlineData("Error")]
    [InlineData("Timeout")]
    public void ARunWhoseOutcomeIsAbnormalIsIncomplete(string outcome)
    {
        string path = Write("run.trx", Trx(outcome, 2, 1, 0, string.Empty, Result("App.Tests.Ok", "Passed")));

        Assert.Equal("run.trx: the run's outcome is " + outcome, Assert.Single(TrxReports.Read([path]).Incomplete));
    }

    [Fact]
    public void ResultsThatTimedOutWereAbortedOrErroredAreAbnormalNotFailedAndNotIgnored()
    {
        string path = Write("a.trx", Trx("Failed", 4, 4, 0, string.Empty,
            Result("App.Tests.Slow", "Timeout"), Result("App.Tests.Cut", "Aborted"), Result("App.Tests.Broke", "Error"), Result("App.Tests.Ok", "Passed")));

        TrxSummary summary = TrxReports.Read([path]);

        Assert.Equal(["App.Tests.Slow (Timeout)", "App.Tests.Cut (Aborted)", "App.Tests.Broke (Error)"], summary.AbnormalTests);
        Assert.Empty(summary.FailedTests);
        Assert.Empty(summary.Incomplete);
    }

    [Fact]
    public void NoReportsMeanNothingRan()
    {
        TrxSummary summary = TrxReports.Read([]);

        Assert.Equal((0, 0, 0), (summary.Total, summary.Executed, summary.Failed));
        Assert.Empty(summary.FailedTests);
        Assert.Empty(summary.Incomplete);
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(root.FullName, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Result(string name, string outcome) =>
        $"""<UnitTestResult executionId="x" testId="y" testName="{name}" computerName="c" outcome="{outcome}" />""";

    private static string RunInfo(string text) =>
        $"""<RunInfo computerName="c" outcome="Error" timestamp="2026-01-01T00:00:00"><Text>{text}</Text></RunInfo>""";

    private static string Trx(int total, int executed, int failed, params string[] results) =>
        Trx(failed > 0 ? "Failed" : "Completed", total, executed, failed, string.Empty, results);

    // The shape of a real VSTest TRX (xUnit v2 on net8), trimmed.
    private static string Trx(string outcome, int total, int executed, int failed, string runInfos, params string[] results) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="1" name="run" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>{string.Concat(results)}</Results>
          <ResultSummary outcome="{outcome}">
            <Counters total="{total}" executed="{executed}" passed="{executed - failed}" failed="{failed}" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
            <RunInfos>{runInfos}</RunInfos>
          </ResultSummary>
        </TestRun>
        """;

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
