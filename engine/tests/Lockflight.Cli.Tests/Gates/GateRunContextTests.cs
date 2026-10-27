using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates;

public sealed class GateRunContextTests
{
    [Theory]
    [InlineData("stop", true, 7200, GateRunContext.StopHookBudgetSeconds)]
    [InlineData("stop", true, 120, 120)]
    [InlineData("stop", false, 7200, 7200)]
    [InlineData("pr", true, 7200, 7200)]
    public void TheStopProfileEndsBeforeTheStopHookIsKilled(string profileName, bool underStopHook, int timeoutSeconds, int expectedBudget)
    {
        Assert.True(GateProfileNames.TryParse(profileName, out GateProfile profile));
        GateRunContext run = Run(profile, timeoutSeconds, underStopHook);

        Assert.Equal(expectedBudget, run.BudgetSeconds);
        Assert.True(GateRunContext.StopHookBudgetSeconds < GateRunContext.StopHookTimeoutSeconds);
    }

    [Fact]
    public void AStepThatRunsOutOfTheCappedBudgetSaysWhy()
    {
        ProcessResult timedOut = new(ProcessRunner.TimedOutExitCode, string.Empty, string.Empty, 540_000, TimedOut: true);

        GateStep? step = StepHealth.Unhealthy(TestStep.Name, timedOut, Run(GateProfile.Stop, 3600, underStopHook: true));

        Assert.Equal(Verdict.Error, step?.Verdict);
        Assert.Contains("540 s cap, below the plugin's 600 s Stop hook timeout", step?.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunInAUnitUsesTheUnitsBuildFileTestFrameworkAndStopFilter()
    {
        string root = Path.GetFullPath("workspace");
        GateRunContext run = GateRunContext.ForTarget(Workspace(root), Path.Combine(root, "services", "orders"), GateProfile.Stop, baseRef: null, TimeProvider.System);

        Assert.Equal(("services/orders", "Orders.sln", "net8.0", "Category!=Integration"), (run.UnitPath, run.BuildFile, run.Settings.TestFramework, run.StopFilter));
    }

    [Fact]
    public void ARunInADirectoryInsideAUnitBuildsTheUnitsBuildFileFromThere()
    {
        string root = Path.GetFullPath("workspace");
        GateRunContext run = GateRunContext.ForTarget(Workspace(root), Path.Combine(root, "services", "orders", "src"), GateProfile.Stop, baseRef: null, TimeProvider.System);

        Assert.Equal(Path.Combine("..", "Orders.sln"), run.BuildFile);
    }

    [Fact]
    public void AUnitWithoutItsOwnSettingsTakesTheRootsFrameworkButNotTheRootsBuildFile()
    {
        string root = Path.GetFullPath("workspace");
        GateRunContext run = GateRunContext.ForTarget(Workspace(root), Path.Combine(root, "services", "billing"), GateProfile.Stop, baseRef: null, TimeProvider.System);

        Assert.Equal(("services/billing", null, "net10.0", null), (run.UnitPath, run.BuildFile, run.Settings.TestFramework, run.StopFilter));
    }

    [Fact]
    public void ARunOutsideEveryUnitUsesTheRootsBuildFile()
    {
        string root = Path.GetFullPath("workspace");
        GateRunContext run = GateRunContext.ForTarget(Workspace(root), root, GateProfile.PullRequest, baseRef: "origin/main", TimeProvider.System);

        Assert.Equal((null, "Shell.slnx", "net10.0", "origin/main"), (run.UnitPath, run.BuildFile, run.Settings.TestFramework, run.BaseRef));
    }

    private static Manifest Workspace(string root) =>
        new(root, [], StopGate: true, StopTarget: null, GateSettings.Default with { TestFramework = "net10.0", BuildFile = "Shell.slnx" })
        {
            Units = [new Unit("services/orders", "Orders.sln", UnitKind.Auto, "net8.0", "Category!=Integration"), new Unit("services/billing", null, UnitKind.Auto, null, null)],
        };

    private static GateRunContext Run(GateProfile profile, int timeoutSeconds, bool underStopHook) =>
        new(".", ".", profile, GateSettings.Default with { TimeoutSeconds = timeoutSeconds }, BaseRef: null, TimeProvider.System, underStopHook);
}
