namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>What checking a typed session code against the one waiting for its command found.</summary>
internal enum SessionCodeState
{
    /// <summary>The code matches the waiting one, which has not expired.</summary>
    Matched = 1,

    /// <summary>No code is waiting for this command: none was shown, or it was already used.</summary>
    NoneWaiting = 2,

    /// <summary>The waiting code is older than its lifetime; it is discarded.</summary>
    Expired = 3,

    /// <summary>The typed code is not the waiting one.</summary>
    WrongCode = 4,

    /// <summary>The typed code is wrong, and so were the attempts before it, up to the limit: the waiting code is discarded.</summary>
    TooManyAttempts = 5,
}
