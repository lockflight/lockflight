namespace Lockflight.Cli.Lifecycle.RedGreen.Snapshots;

/// <summary>
/// The tree a red was accepted on, as a commit object: the <see cref="Commit"/>, or why none was taken (<see cref="NotTaken"/>).
/// <see cref="UntrackedNonSource"/> names the untracked files it holds that do not look like source, relative to the repository and
/// '/'-separated, so a stray local file that went into git's object store is visible.
/// </summary>
internal sealed record RedSnapshot(string? Commit, string? NotTaken, IReadOnlyList<string> UntrackedNonSource)
{
    public static RedSnapshot Taken(string commit, IReadOnlyList<string> untrackedNonSource) => new(commit, null, untrackedNonSource);

    public static RedSnapshot Skipped(string reason) => new(null, reason, []);
}
