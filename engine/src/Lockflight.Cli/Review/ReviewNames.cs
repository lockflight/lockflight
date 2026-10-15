namespace Lockflight.Cli.Review;

/// <summary>Contract spellings of review enums in the reviewer's JSON and in evidence files.</summary>
internal static class ReviewNames
{
    public static string ToName(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Blocker => "blocker",
        FindingSeverity.Major => "major",
        FindingSeverity.Minor => "minor",
        FindingSeverity.Nit => "nit",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null),
    };

    public static FindingSeverity ParseSeverity(string? name) => name switch
    {
        "blocker" => FindingSeverity.Blocker,
        "major" => FindingSeverity.Major,
        "minor" => FindingSeverity.Minor,
        _ => FindingSeverity.Nit,
    };

    public static string ToName(FindingStatus status) => status switch
    {
        FindingStatus.Reported => "reported",
        FindingStatus.Confirmed => "confirmed",
        FindingStatus.NotReproduced => "not-reproduced",
        FindingStatus.Unverified => "unverified",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
