namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>What a need of a blocked report asks for (<c>needs[].action</c>); <see cref="Unknown"/> keeps an unknown value as written.</summary>
internal enum BlockedAction
{
    /// <summary>A value this engine does not know.</summary>
    Unknown = 0,

    /// <summary>Review and approve an artifact (<c>approve</c>).</summary>
    Approve = 1,

    /// <summary>Answer the report's questions (<c>answer</c>).</summary>
    Answer = 2,

    /// <summary>Review a change to a protected file (<c>review-protected-change</c>).</summary>
    ReviewProtectedChange = 3,

    /// <summary>Accept a baseline (<c>accept-baseline</c>).</summary>
    AcceptBaseline = 4,

    /// <summary>Raise a limit in the policy (<c>raise-limit</c>).</summary>
    RaiseLimit = 5,

    /// <summary>Fix the configuration (<c>fix-config</c>).</summary>
    FixConfig = 6,

    /// <summary>Grant access (<c>grant-access</c>).</summary>
    GrantAccess = 7,

    /// <summary>Install or update the engine (<c>update-engine</c>).</summary>
    UpdateEngine = 8,
}
