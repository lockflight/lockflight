namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>When the hashes in the record of what the person could last see (<see cref="SeenArtifacts"/>) were taken.</summary>
internal enum SeenWhen
{
    /// <summary>The agent's turn ended: the Stop hook took them.</summary>
    TurnEnded = 1,

    /// <summary><c>/lockflight:approve</c> showed the person the content instead of approving it.</summary>
    Shown = 2,
}
