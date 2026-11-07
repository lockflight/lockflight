using Lockflight.Cli.Config;
using Lockflight.Cli.Doctor;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates.WindowsTargets;

/// <summary>
/// The build and test steps of a run that leaves out Windows-targeted tests (a person's run off Windows), against one that does
/// not (on Windows, or the CI judge, which still builds off Windows with Windows targeting enabled), on a WPF client and ASP.NET Core server solution; the .NET SDK is replaced by canned results.
/// </summary>
public sealed class OffWindowsGateStepsTests
{
    private const string PassingTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results><UnitTestResult testName="Desk.Server.Tests.PriceListTests.APenCosts" outcome="Passed" /></Results>
          <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary>
        </TestRun>
        """;

    private readonly List<IReadOnlyList<string>> calls = [];

    [Fact]
    public async Task OffWindowsTheWindowsTestProjectsAreLeftOutWithANote()
    {
        using DesktopSolution solution = new();

        GateStepResult result = await new TestStep().RunAsync(Run(solution, leavesOut: true), GateRunFacts.None, TestContext.Current.CancellationToken);

        Assert.Equal(Verdict.Pass, result.Step.Verdict);
        Assert.EndsWith("left out off Windows: Desk.Client.Tests (they target Windows and cannot start here; they run on Windows and on a Windows CI runner)", result.Step.Detail, StringComparison.Ordinal);
        string filter = Assert.Single(calls)[1];
        Assert.EndsWith(".off-windows.slnf", filter, StringComparison.Ordinal);
        string filterText = File.ReadAllText(filter);
        Assert.Contains(@"src\\Desk.Server\\Desk.Server.csproj", filterText, StringComparison.Ordinal);
        Assert.Contains(@"tests\\Desk.Server.Tests\\Desk.Server.Tests.csproj", filterText, StringComparison.Ordinal);
        Assert.DoesNotContain("Desk.Client", filterText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenEveryTestProjectTargetsWindowsTheTestStepIsNotApplicable()
    {
        using DesktopSolution solution = new(withServerTests: false);

        GateStepResult result = await new TestStep().RunAsync(Run(solution, leavesOut: true), GateRunFacts.None, TestContext.Current.CancellationToken);

        Assert.Equal(Verdict.NotApplicable, result.Step.Verdict);
        Assert.StartsWith("no test project left to run: left out off Windows: Desk.Client.Tests", result.Step.Detail, StringComparison.Ordinal);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task ARunThatLeavesNothingOutTestsTheWholeSolution()
    {
        using DesktopSolution solution = new();

        GateStepResult result = await new TestStep().RunAsync(Run(solution, leavesOut: false), GateRunFacts.None, TestContext.Current.CancellationToken);

        Assert.Equal("total 1", result.Step.Detail);
        Assert.Equal("--no-build", Assert.Single(calls)[1]);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public async Task OffWindowsTheBuildEnablesWindowsTargetingForAPersonAndForTheJudge(bool offWindows, bool leavesOut, bool expected)
    {
        using DesktopSolution solution = new();

        await new BuildStep().RunAsync(Run(solution, leavesOut) with { OffWindows = offWindows }, GateRunFacts.None, TestContext.Current.CancellationToken);

        Assert.Equal(expected, Assert.Single(calls).Contains("-p:EnableWindowsTargeting=true"));
    }

    [Fact]
    public async Task ASolutionWithoutWindowsProjectsBuildsAsBefore()
    {
        using DesktopSolution solution = new();
        solution.Write("src/Desk.Client/Desk.Client.csproj", DesktopSolution.ServerProject);
        solution.Write("tests/Desk.Client.Tests/Desk.Client.Tests.csproj", DesktopSolution.ServerTestProject);

        await new BuildStep().RunAsync(Run(solution, leavesOut: true), GateRunFacts.None, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("-p:EnableWindowsTargeting=true", Assert.Single(calls));
    }

    [Fact]
    public void DoctorExplainsTheLeftOutTestsOffWindowsOnly()
    {
        using DesktopSolution solution = new();

        DoctorCheck? offWindows = WindowsTargetCheck.Check(solution.Root, onWindows: false);

        Assert.Equal(CheckStatus.Info, offWindows?.Status);
        Assert.Contains("2 projects target Windows", offWindows!.Detail, StringComparison.Ordinal);
        Assert.Contains("EnableWindowsTargeting=true", offWindows.Detail, StringComparison.Ordinal);
        Assert.Contains("(Desk.Client.Tests)", offWindows.Detail, StringComparison.Ordinal);
        Assert.Null(WindowsTargetCheck.Check(solution.Root, onWindows: true));
    }

    private GateRunContext Run(DesktopSolution solution, bool leavesOut) =>
        new(solution.Root, solution.Root, GateProfile.Stop, GateSettings.Default, BaseRef: null, TimeProvider.System, UnderStopHook: false)
        {
            LeavesOutWindowsTests = leavesOut,
            OffWindows = leavesOut,
            Processes = (fileName, arguments, workingDirectory, timeLimit, cancellationToken) => FakeDotnetAsync(arguments),
        };

    private Task<ProcessResult> FakeDotnetAsync(IReadOnlyList<string> arguments)
    {
        calls.Add(arguments);
        if (arguments[0] == "test")
        {
            string results = arguments[arguments.ToList().IndexOf("--results-directory") + 1];
            Directory.CreateDirectory(results);
            File.WriteAllText(Path.Combine(results, "lockflight.trx"), PassingTrx);
        }

        return Task.FromResult(new ProcessResult(0, "Build succeeded.", string.Empty, 0, TimedOut: false));
    }
}
