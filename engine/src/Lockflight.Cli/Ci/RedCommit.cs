namespace Lockflight.Cli.Ci;

/// <summary>
/// A commit in the pull request that carries a <c>Lockflight-Red</c> trailer. <see cref="Label"/> is the cycle label (null when
/// malformed); <see cref="Malformed"/> says why the commit is not a red; <see cref="Superseded"/> marks an earlier commit with a label
/// a later commit carries again; <see cref="ForkPoint"/> is the merge base of the commit and the target branch, filled in by the plan.
/// </summary>
internal sealed record RedCommit(string Commit, string Subject, string CommittedAt, string? Label, string? Malformed, bool Superseded)
{
    public string? ForkPoint { get; init; }

    /// <summary>Whether this commit is a cycle's red: well formed and not superseded.</summary>
    public bool IsCycle => Malformed is null && !Superseded;
}
