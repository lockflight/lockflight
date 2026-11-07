namespace Lockflight.Cli.Processes.Survivors;

/// <summary>The evidence's names for <see cref="SweepMechanism"/>.</summary>
internal static class SweepMechanismNames
{
    public static string ToName(SweepMechanism mechanism) => mechanism switch
    {
        SweepMechanism.Subreaper => "subreaper",
        SweepMechanism.Job => "job",
        SweepMechanism.Marker => "marker",
        _ => "none",
    };
}
