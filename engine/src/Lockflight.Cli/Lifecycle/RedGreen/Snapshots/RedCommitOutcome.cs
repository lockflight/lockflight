namespace Lockflight.Cli.Lifecycle.RedGreen.Snapshots;

/// <summary>
/// What the engine's red commit came to: the commit, or why git did not make it, and a note for the person.
/// <see cref="TreeChanged"/> says a commit hook changed the tree, so the commit is not the tree the red judged.
/// </summary>
internal sealed record RedCommitOutcome(string? Commit, string Message)
{
    public bool TreeChanged { get; init; }
}
