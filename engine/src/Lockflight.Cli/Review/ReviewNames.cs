namespace Lockflight.Cli.Review;

/// <summary>Contract spellings of review enums in the reviewer's JSON, in evidence files and in command output.</summary>
internal static class ReviewNames
{
    /// <summary>The one reviewer today: the whole change against the spec, with the engine's general prompt.</summary>
    public const string GeneralDimension = "general";

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

    public static string ToName(FindingOutcome outcome) => outcome switch
    {
        FindingOutcome.Reported => "reported",
        FindingOutcome.Confirmed => "confirmed",
        FindingOutcome.Rejected => "rejected",
        FindingOutcome.Unverified => "unverified",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    /// <summary>The outcome an evidence file names; an unknown name reads as unverified (toward a person, never toward a pass).</summary>
    public static FindingOutcome ParseOutcome(string? name) => name switch
    {
        "reported" => FindingOutcome.Reported,
        "confirmed" => FindingOutcome.Confirmed,
        "rejected" => FindingOutcome.Rejected,
        _ => FindingOutcome.Unverified,
    };

    public static string ToName(ReproductionResult result) => result switch
    {
        ReproductionResult.None => "none",
        ReproductionResult.NotRun => "not-run",
        ReproductionResult.Failed => "failed",
        ReproductionResult.Passed => "passed",
        ReproductionResult.DidNotCompile => "did-not-compile",
        ReproductionResult.Inconclusive => "inconclusive",
        ReproductionResult.RepairedPassed => "repaired-passed",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };

    public static string ToName(FindingState state) => state switch
    {
        FindingState.Open => "open",
        FindingState.Fixed => "fixed",
        FindingState.Dismissed => "dismissed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static FindingState? ParseState(string? name) => name switch
    {
        "open" => FindingState.Open,
        "fixed" => FindingState.Fixed,
        "dismissed" => FindingState.Dismissed,
        _ => null,
    };
}
