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

    private static GateRunContext Run(GateProfile profile, int timeoutSeconds, bool underStopHook) =>
        new(".", ".", profile, GateSettings.Default with { TimeoutSeconds = timeoutSeconds }, BaseRef: null, TimeProvider.System, underStopHook);
}
