using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// Test runs the test gate must not PASS — a failing test, every test skipped, no test at all — judged as the gate judges a real
/// run, on both test platforms. The harmless twin, a run whose tests pass, must PASS. The full mode builds and runs real ones
/// (<see cref="CanaryProject"/>).
/// </summary>
internal static class TestGateCanaries
{
    private const string TrxFileName = "canary.trx";

    private static readonly PlantedTestRun Passing = new(
        "a passing test", 0, "Test run summary: Passed!\n  total: 2\n  failed: 0\n  succeeded: 2\n  skipped: 0\n", 0, Trx(total: 2, executed: 2, failed: 0, outcome: "Completed", results: ""));

    private static readonly PlantedTestRun[] Violations =
    [
        new("a failing test", 2, "failed Canary.Tests.CanaryTests.Fails (3ms)\nTest run summary: Failed!\n  total: 2\n  failed: 1\n  succeeded: 1\n  skipped: 0\n",
            1, Trx(total: 2, executed: 2, failed: 1, outcome: "Failed", results: """<UnitTestResult testName="Canary.Tests.CanaryTests.Fails" outcome="Failed" />""")),
        new("every test skipped", 0, "skipped Canary.Tests.CanaryTests.Later (0ms)\nTest run summary: Zero tests ran\n  total: 2\n  failed: 0\n  succeeded: 0\n  skipped: 2\n",
            0, Trx(total: 2, executed: 0, failed: 0, outcome: "Completed", results: """<UnitTestResult testName="Canary.Tests.CanaryTests.Later" outcome="NotExecuted" />""")),
        new("zero tests", 8, "Test run summary: Zero tests ran\n  total: 0\n  failed: 0\n  succeeded: 0\n  skipped: 0\n", 0, Trx(total: 0, executed: 0, failed: 0, outcome: "Completed", results: "")),
        new("zero tests and no report", 0, "Test run summary: Passed!\n", 0, null),
    ];

    public static IReadOnlyList<CanaryReport> Run(CanaryScratch scratch)
    {
        GateStep[] twin = Judge(Passing, scratch);
        if (twin.FirstOrDefault(step => step.Verdict != Verdict.Pass) is GateStep notPassing)
        {
            return [new CanaryReport("test gate: a passing run passes", CanaryResult.Broken,
                $"a run whose tests pass is {VerdictNames.ToContractName(notPassing.Verdict)} ({notPassing.Detail}), so a rejection proves nothing")];
        }

        List<CanaryReport> reports = [];
        foreach (PlantedTestRun planted in Violations)
        {
            GateStep[] steps = Judge(planted, scratch);
            string[] runners = ["Testing Platform", "VSTest"];
            for (int index = 0; index < steps.Length; index++)
            {
                reports.Add(Report($"test gate ({runners[index]}): {planted.Case} is not a PASS", steps[index]));
            }
        }

        return reports;
    }

    /// <summary>A gate step that is anything but PASS is caught: FAIL, NOT-EARNED and ERROR all stop the agent's turn.</summary>
    public static CanaryReport Report(string name, GateStep step) => step.Verdict == Verdict.Pass
        ? new CanaryReport(name, CanaryResult.Missed, $"PASS ({step.Detail})")
        : new CanaryReport(name, CanaryResult.Caught, $"{VerdictNames.ToContractName(step.Verdict)}: {step.Detail}");

    /// <summary>The planted run judged on Microsoft.Testing.Platform, then on VSTest from its TRX report.</summary>
    private static GateStep[] Judge(PlantedTestRun planted, CanaryScratch scratch)
    {
        GateStep platform = TestStep.JudgeTestingPlatform(new ProcessResult(planted.PlatformExitCode, planted.PlatformOutput, string.Empty, 0, TimedOut: false), stopFilter: null);
        string[] reports = [];
        if (planted.Trx is string trx)
        {
            string path = scratch.PathOf(TrxFileName);
            File.WriteAllText(path, trx);
            reports = [path];
        }

        GateStep vstest = TestStep.JudgeVSTest(
            new ProcessResult(planted.VSTestExitCode, string.Empty, string.Empty, 0, TimedOut: false), reports.Length, TrxReports.Read(reports), () => new HashSet<string>(StringComparer.Ordinal), stopFilter: null);
        return [platform, vstest];
    }

    private static string Trx(int total, int executed, int failed, string outcome, string results) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>{results}</Results>
          <ResultSummary outcome="{outcome}">
            <Counters total="{total}" executed="{executed}" passed="{executed - failed}" failed="{failed}" error="0" timeout="0" aborted="0" />
          </ResultSummary>
        </TestRun>
        """;
}
