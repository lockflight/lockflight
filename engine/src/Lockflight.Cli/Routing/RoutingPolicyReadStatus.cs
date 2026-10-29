namespace Lockflight.Cli.Routing;

/// <summary>How reading the base branch's routing policy ended; only <see cref="Read"/> and <see cref="NoRoutingSection"/> carry a policy.</summary>
internal enum RoutingPolicyReadStatus
{
    /// <summary>The base branch's manifest has a <c>routing:</c> section, and it was read.</summary>
    Read = 1,

    /// <summary>The base branch has no manifest, or one without <c>routing:</c>: the default preset applies, and the output says so.</summary>
    NoRoutingSection = 2,

    /// <summary>The base ref names no commit: NOT-EARNED in CI.</summary>
    NoBase = 3,

    /// <summary>A git call failed: ERROR.</summary>
    GitFailed = 4,

    /// <summary>The base branch's manifest is invalid: ERROR, never "default routing".</summary>
    InvalidPolicy = 5,
}
