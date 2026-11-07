using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes.Survivors;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Tests.Gates;

public sealed class GateSurvivorsTests
{
    private static readonly GateResult Passed = new("/repo", GateProfile.PullRequest, Verdict.Pass, 3, [new GateStep("build", 0, Verdict.Pass, ReasonCode.None, 10, string.Empty)]);

    [Theory]
    [InlineData("pr", "true", true)]
    [InlineData("pr", null, false)]
    [InlineData("stop", "true", false)]
    public void OnlyAPrRunInCiSweeps(string profile, string? ci, bool sweeps)
    {
        Assert.True(GateProfileNames.TryParse(profile, out GateProfile parsed));
        Dictionary<string, string> variables = ci is null ? [] : new() { [GateSurvivors.CiVariable] = ci };

        Assert.Equal(sweeps, GateSurvivors.Sweeps(parsed, EnvironmentReader.Of(variables), current: null));
    }

    [Fact]
    public void AGateRunInsideTheJudgesSweepDoesNotSweepAgain()
    {
        using ProcessSweep judge = new(new NoProcessScope("the judge's"));

        Assert.False(GateSurvivors.Sweeps(GateProfile.PullRequest, EnvironmentReader.Of(new Dictionary<string, string> { [GateSurvivors.CiVariable] = "true" }), judge));
    }

    [Fact]
    public void AProcessThatCouldNotBeStoppedAddsAnErrorRow()
    {
        GateResult result = GateSurvivors.Apply(Passed, Report(SurvivorOutcome.NotStopped));

        Assert.Equal(Verdict.Error, result.Verdict);
        GateStep row = result.Steps[^1];
        Assert.Equal((GateSurvivors.StepId, Verdict.Error, ReasonCode.SurvivorNotStopped), (row.Id, row.Verdict, row.Reason));
    }

    [Fact]
    public void AStoppedProcessLeavesTheResultAsItWasAndIsListed()
    {
        ProcessesReport processes = Report(SurvivorOutcome.Stopped);

        Assert.Same(Passed, GateSurvivors.Apply(Passed, processes));
        Assert.Equal(["Left running (stopped): 'sleep 600' (pid 4711, after 'dotnet test')"], GateSurvivors.Lines(processes));
    }

    [Fact]
    public void WithoutAMechanismTheRunSaysProcessesWereNotChecked() =>
        Assert.Equal(
            ["Processes left running were not checked: not on this system"],
            GateSurvivors.Lines(new ProcessesReport(SweepMechanism.None, "not on this system", 0, [], 0, [])));

    private static ProcessesReport Report(SurvivorOutcome outcome) =>
        new(SweepMechanism.Subreaper, null, 4, [new Survivor(SweepRegion.Setup, "dotnet test", new SweptProcess(4711, "/usr/bin/sleep", "sleep 600", "runner", outcome))], 0, [])
        {
            NotStopped = outcome == SurvivorOutcome.NotStopped ? 1 : 0,
        };
}
