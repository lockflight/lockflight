namespace Lockflight.Cli.Review;

/// <summary>
/// What a review compares against: the ref it was given, the commit that ref names, and the merge base of that ref and HEAD.
/// A null commit or merge base means git could not name it.
/// </summary>
internal sealed record ReviewBase(string Ref, string? Commit, string? MergeBase)
{
    /// <summary>Whether the branch contains the base: the base has not moved on since the branch left it.</summary>
    public bool IsMergeBase => Commit is not null && Commit == MergeBase;
}
