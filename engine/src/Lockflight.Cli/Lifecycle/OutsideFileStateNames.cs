namespace Lockflight.Cli.Lifecycle;

/// <summary>The evidence names of <see cref="OutsideFileState"/>.</summary>
internal static class OutsideFileStateNames
{
    public static string ToName(OutsideFileState state) => state switch
    {
        OutsideFileState.Names => "names",
        OutsideFileState.Missing => "missing",
        OutsideFileState.DoesNotName => "does-not-name",
        OutsideFileState.OutsideProject => "outside-project",
        OutsideFileState.InSpecs => "in-specs",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown state."),
    };
}
