namespace Lockflight.Cli.Routing;

/// <summary>The contract spellings of <see cref="SignalStrength"/>: <c>heuristic</c>, <c>boundary</c>.</summary>
internal static class SignalStrengthNames
{
    public static string ToName(SignalStrength strength) => strength switch
    {
        SignalStrength.Heuristic => "heuristic",
        SignalStrength.Boundary => "boundary",
        _ => throw new ArgumentOutOfRangeException(nameof(strength), strength, "Unknown signal strength."),
    };
}
