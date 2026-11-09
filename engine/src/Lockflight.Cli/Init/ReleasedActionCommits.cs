namespace Lockflight.Cli.Init;

/// <summary>
/// Every commit an engine built from this repository's main branch has pinned the CI judge's action to
/// (<see cref="Templates.ActionCommit"/> over time): the releases' pins and the development builds' between them. <c>update</c>
/// replaces a workflow's pin only when it is one of these or the current one, so a pin a person raised by hand is an edit it
/// shows, never lowers; <c>doctor</c> warns about a pin that is none of them. A change that moves <c>ActionCommit</c> adds the
/// commit it replaces here.
/// </summary>
internal static class ReleasedActionCommits
{
    /// <summary>The pins, newest first; 0.3.0 and 0.3.1 pinned <c>6960ea75</c>.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "5d43a5b6a33b0b480a1e551d5b9c06963fa4498a",
        "ad1778453619e848f073620a502cb2948fcf2b6e",
        "c3c62593abf11dbdda437248c0fc72d55dff034e",
        "973d412bba8cd6fb0657f118ef43fb36ef8bbf54",
        "3fd126b39a97d347ef16d680ef1e7f65e3cb9495",
        "6960ea75096a0754fed65038d5557d690526172d",
    ];

    /// <summary>Whether <paramref name="commit"/> is the current pin or one an engine of this repository has written.</summary>
    public static bool IsKnown(string commit) =>
        string.Equals(commit, Templates.ActionCommit, StringComparison.Ordinal) || All.Contains(commit, StringComparer.Ordinal);
}
