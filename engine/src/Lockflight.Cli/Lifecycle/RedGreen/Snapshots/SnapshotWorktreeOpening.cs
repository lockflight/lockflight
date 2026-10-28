namespace Lockflight.Cli.Lifecycle.RedGreen.Snapshots;

/// <summary>A temporary worktree of a red-time snapshot: the <see cref="Worktree"/>, or the <see cref="Problem"/> that says why there is none.</summary>
internal sealed record SnapshotWorktreeOpening(SnapshotWorktree? Worktree, string? Problem)
{
    public static SnapshotWorktreeOpening Opened(SnapshotWorktree worktree) => new(worktree, null);

    public static SnapshotWorktreeOpening Failed(string problem) => new(null, problem);
}
