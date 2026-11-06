namespace Lockflight.Cli.Processes.Survivors;

/// <summary>The evidence's names for <see cref="SweepRegion"/>.</summary>
internal static class SweepRegionNames
{
    public static string ToName(SweepRegion region) => region switch
    {
        SweepRegion.Head => "head",
        SweepRegion.Base => "base",
        SweepRegion.Red => "red",
        SweepRegion.Rechecks => "rechecks",
        SweepRegion.Gates => "gates",
        SweepRegion.Final => "final",
        _ => "setup",
    };
}
