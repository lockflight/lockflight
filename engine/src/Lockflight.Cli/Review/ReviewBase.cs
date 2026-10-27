namespace Lockflight.Cli.Review;

/// <summary>
/// What a review compares against: the ref it was given and the commit it names, the branch the change targets, and the merge base
/// of HEAD and that target. A null commit or merge base means git could not name it.
/// </summary>
internal sealed record ReviewBase(string Ref, string? Commit, string Target, string? MergeBase)
{
    /// <summary>Whether the base is exactly where the branch left the target: neither an older nor a newer commit.</summary>
    public bool IsMergeBase => Commit is not null && Commit == MergeBase;
}
