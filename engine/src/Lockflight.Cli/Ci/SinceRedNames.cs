namespace Lockflight.Cli.Ci;

/// <summary>The evidence spellings of <see cref="SinceRed"/>.</summary>
internal static class SinceRedNames
{
    public static string ToName(SinceRed state) => state switch
    {
        SinceRed.Unchanged => "unchanged",
        SinceRed.AmendAccepted => "amend-accepted",
        SinceRed.AmendRejected => "amend-rejected",
        SinceRed.AmendUncheckable => "amend-uncheckable",
        SinceRed.MissingAtHead => "missing-at-head",
        SinceRed.AmendChangedExpectation => "amend-changed-expectation",
        SinceRed.AmendWeakened => "amend-weakened",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown since-red state."),
    };
}
