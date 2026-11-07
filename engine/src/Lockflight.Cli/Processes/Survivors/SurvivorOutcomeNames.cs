namespace Lockflight.Cli.Processes.Survivors;

/// <summary>The evidence's names for <see cref="SurvivorOutcome"/>.</summary>
internal static class SurvivorOutcomeNames
{
    public static string ToName(SurvivorOutcome outcome) => outcome switch
    {
        SurvivorOutcome.Stopped => "stopped",
        SurvivorOutcome.Exited => "exited",
        SurvivorOutcome.Observed => "observed",
        _ => "not-stopped",
    };
}
