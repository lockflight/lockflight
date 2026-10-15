namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>Whether a recorded approval still covers the artifacts on disk.</summary>
internal enum ApprovalState
{
    /// <summary>No approval has been recorded for this phase.</summary>
    Missing = 1,

    /// <summary>The approval matches every artifact it binds.</summary>
    Valid = 2,

    /// <summary>An approved artifact changed (or appeared, or vanished) after approval; it must be approved again.</summary>
    Stale = 3,

    /// <summary>
    /// The record cannot approve this phase: it is unreadable, or it names another feature or phase (a record copied from elsewhere).
    /// </summary>
    Invalid = 4,
}
