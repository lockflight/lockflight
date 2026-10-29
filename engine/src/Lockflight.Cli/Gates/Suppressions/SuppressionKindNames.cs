namespace Lockflight.Cli.Gates.Suppressions;

/// <summary>How a finding names its <see cref="SuppressionKind"/>: as it is written in the file.</summary>
internal static class SuppressionKindNames
{
    public static string ToDisplay(SuppressionKind kind) => kind switch
    {
        SuppressionKind.Pragma => "#pragma warning disable",
        SuppressionKind.SuppressMessage => "[SuppressMessage]",
        SuppressionKind.NoWarn => "<NoWarn>",
        SuppressionKind.WarningsNotAsErrors => "<WarningsNotAsErrors>",
        SuppressionKind.LoweredSeverity => "lowered severity",
        SuppressionKind.NullableDisable => "#nullable disable",
        SuppressionKind.ExcludeFromCodeCoverage => "[ExcludeFromCodeCoverage]",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown suppression kind."),
    };
}
