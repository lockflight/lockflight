using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// The self-test's test gate canary: a run with a failing test, judged as the gate judges a real run on Microsoft.Testing.Platform
/// and, from its TRX report, on VSTest, must not PASS on either. Its harmless twin, a run whose tests pass, must PASS. Nothing is
/// built; the engine's canary suite also plants every test skipped and no test, and runs the real gate on a generated project.
/// </summary>
internal static class TestGateCanary
{
    public const string Name = "test gate: a failing test is not a PASS (Testing Platform and VSTest)";

    private const string PassingOutput = "Test run summary: Passed!\n  total: 2\n  failed: 0\n  succeeded: 2\n  skipped: 0\n";

    private const string FailingOutput = "failed Canary.Tests.CanaryTests.Fails (3ms)\nTest run summary: Failed!\n  total: 2\n  failed: 1\n  succeeded: 1\n  skipped: 0\n";

    private const string FailingTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results><UnitTestResult testName="Canary.Tests.CanaryTests.Fails" outcome="Failed" /></Results>
          <ResultSummary outcome="Failed">
            <Counters total="2" executed="2" passed="1" failed="1" error="0" timeout="0" aborted="0" />
          </ResultSummary>
        </TestRun>
        """;

    public static CanaryReport Run(CanaryScratch scratch)
    {
        GateStep twin = TestStep.JudgeTestingPlatform(Ran(0, PassingOutput), stopFilter: null);
        if (twin.Verdict != Verdict.Pass)
        {
            return new CanaryReport(Name, CanaryResult.Broken,
                $"a run whose tests pass is {VerdictNames.ToContractName(twin.Verdict)} ({twin.Detail}), so a rejection proves nothing");
        }

        CanaryReport platform = CanaryReport.NotAPass(Name, TestStep.JudgeTestingPlatform(Ran(2, FailingOutput), stopFilter: null));
        if (platform.Result != CanaryResult.Caught)
        {
            return platform with { Detail = $"Testing Platform: {platform.Detail}" };
        }

        string trx = scratch.PathOf("canary.trx");
        File.WriteAllText(trx, FailingTrx);
        GateStep vstest = TestStep.JudgeVSTest(Ran(1, string.Empty), 1, TrxReports.Read([trx]), () => new HashSet<string>(StringComparer.Ordinal), stopFilter: null);
        CanaryReport report = CanaryReport.NotAPass(Name, vstest);
        return report.Result == CanaryResult.Caught ? platform : report with { Detail = $"VSTest: {report.Detail}" };
    }

    private static ProcessResult Ran(int exitCode, string output) => new(exitCode, output, string.Empty, 0, TimedOut: false);
}
