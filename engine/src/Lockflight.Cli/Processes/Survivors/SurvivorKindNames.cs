namespace Lockflight.Cli.Processes.Survivors;

/// <summary>The evidence's names for <see cref="SurvivorKind"/>.</summary>
internal static class SurvivorKindNames
{
    public static string ToName(SurvivorKind kind) => kind switch
    {
        SurvivorKind.Container => "container",
        _ => "process",
    };
}
