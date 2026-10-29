namespace Lockflight.Cli.Routing;

/// <summary>Whether a signal comes from base-branch policy (boundary) or a text heuristic (guardrail).</summary>
internal enum SignalStrength
{
    /// <summary>A heuristic over the diff's text: a guardrail an agent could defeat.</summary>
    Heuristic = 1,

    /// <summary>A rule read from the base branch's policy, which the pull request cannot change.</summary>
    Boundary = 2,
}
