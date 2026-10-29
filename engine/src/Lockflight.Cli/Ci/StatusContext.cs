namespace Lockflight.Cli.Ci;

/// <summary>
/// The status contexts the report publishes. Each takes the verdict of its own checks, so a ruleset can require the gates
/// without the red/green evidence and add the judge when the team is ready.
/// </summary>
internal enum StatusContext
{
    /// <summary><c>gates</c>: the <c>pr</c> gates plus green at head, the meaning the context had before the judge.</summary>
    Gates = 1,

    /// <summary><c>lockflight/judge</c>: red, tests since red, existing tests changed, host tampering, trace, review and approvals.</summary>
    Judge = 2,
}
