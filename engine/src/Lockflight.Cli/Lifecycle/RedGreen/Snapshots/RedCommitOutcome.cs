namespace Lockflight.Cli.Lifecycle.RedGreen.Snapshots;

/// <summary>What the engine's red commit came to: the commit, or why git did not make it, and a note for the person.</summary>
internal sealed record RedCommitOutcome(string? Commit, string Message);
