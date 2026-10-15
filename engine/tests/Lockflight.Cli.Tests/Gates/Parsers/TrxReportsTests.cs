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

        Assert.Equal(new TrxSummary(9, 8, 2, summary.FailedTests), summary);
        Assert.Equal(["App.Tests.CalcTests.AddsTwoNumbers", "App.Tests.CalcTests.Theory(x: 1)"], summary.FailedTests);
    }

    [Fact]
    public void NoReportsMeanNothingRan()
    {
        Assert.Equal(new TrxSummary(0, 0, 0, []), TrxReports.Read([]) with { FailedTests = [] });
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(root.FullName, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Result(string name, string outcome) =>
        $"""<UnitTestResult executionId="x" testId="y" testName="{name}" computerName="c" outcome="{outcome}" />""";

    // The shape of a real VSTest TRX (xUnit v2 on net8), trimmed.
    private static string Trx(int total, int executed, int failed, params string[] results) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="1" name="run" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>{string.Concat(results)}</Results>
          <ResultSummary outcome="Failed">
            <Counters total="{total}" executed="{executed}" passed="{executed - failed}" failed="{failed}" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
          </ResultSummary>
        </TestRun>
        """;

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
