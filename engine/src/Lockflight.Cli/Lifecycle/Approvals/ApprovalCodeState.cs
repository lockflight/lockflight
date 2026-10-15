namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>What checking a typed approval code against the one waiting for the phase found.</summary>
internal enum ApprovalCodeState
{
    /// <summary>The code matches the waiting one, which has not expired.</summary>
    Matched = 1,

    /// <summary>No code is waiting for this phase: none was shown, or it was already used.</summary>
    NoneWaiting = 2,

    /// <summary>The waiting code is older than its lifetime; it is discarded.</summary>
    Expired = 3,

    /// <summary>The typed code is not the waiting one.</summary>
    WrongCode = 4,
}
