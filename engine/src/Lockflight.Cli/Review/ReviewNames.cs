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
        FindingOutcome.Contested => "contested",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    /// <summary>The outcome an evidence file names; an unknown name reads as unverified (toward a person, never toward a pass).</summary>
    public static FindingOutcome ParseOutcome(string? name) => name switch
    {
        "reported" => FindingOutcome.Reported,
        "confirmed" => FindingOutcome.Confirmed,
        "rejected" => FindingOutcome.Rejected,
        "contested" => FindingOutcome.Contested,
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

    public static string ToName(SpecAnchor anchor) => anchor switch
    {
        SpecAnchor.None => "none",
        SpecAnchor.Anchored => "anchored",
        SpecAnchor.NotAnchored => "not-anchored",
        _ => throw new ArgumentOutOfRangeException(nameof(anchor), anchor, null),
    };

    public static SpecAnchor ParseSpecAnchor(string? name) => name switch
    {
        "anchored" => SpecAnchor.Anchored,
        "not-anchored" => SpecAnchor.NotAnchored,
        _ => SpecAnchor.None,
    };

    /// <summary>How output and the pull request summary say a blocker/major finding's spec anchor: "spec quote anchored", …</summary>
    public static string Describe(SpecAnchor anchor) => anchor switch
    {
        SpecAnchor.Anchored => "spec quote anchored",
        SpecAnchor.NotAnchored => "spec quote not anchored",
        _ => "no spec quote",
    };

    public static string ToName(FixCheckResult result) => result switch
    {
        FixCheckResult.None => "none",
        FixCheckResult.Held => "held",
        FixCheckResult.BreaksOwnTest => "breaks-own-test",
        FixCheckResult.DidNotApply => "did-not-apply",
        FixCheckResult.DidNotBuild => "did-not-build",
        FixCheckResult.DidNotFix => "did-not-fix",
        FixCheckResult.NotRun => "not-run",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };

    public static FixCheckResult ParseFixCheck(string? name) => name switch
    {
        "held" => FixCheckResult.Held,
        "breaks-own-test" => FixCheckResult.BreaksOwnTest,
        "did-not-apply" => FixCheckResult.DidNotApply,
        "did-not-build" => FixCheckResult.DidNotBuild,
        "did-not-fix" => FixCheckResult.DidNotFix,
        "not-run" => FixCheckResult.NotRun,
        _ => FixCheckResult.None,
    };

    /// <summary>
    /// How output and the pull request summary say what the fix check of a confirmed or contested finding showed: "fix-checked",
    /// "the reviewer's fix breaks the change's own test A.B", …; null for findings a fix check does not concern.
    /// </summary>
    public static string? DescribeFixCheck(ReviewFinding finding) => finding.Outcome is not (FindingOutcome.Confirmed or FindingOutcome.Contested)
        ? null
        : finding.FixCheck switch
        {
            FixCheckResult.Held => "fix-checked",
            FixCheckResult.BreaksOwnTest => $"the reviewer's fix breaks the change's own test{(finding.ConflictingTests.Count == 1 ? string.Empty : "s")} {string.Join(", ", finding.ConflictingTests)}",
            FixCheckResult.DidNotApply => "the reviewer's fix did not apply",
            FixCheckResult.DidNotBuild => "the reviewer's fix did not build",
            FixCheckResult.DidNotFix => "not fix-checked: the reviewer's fix left the reproduction failing",
            FixCheckResult.NotRun => "not fix-checked: the fixed build did not run",
            _ => "not fix-checked",
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
