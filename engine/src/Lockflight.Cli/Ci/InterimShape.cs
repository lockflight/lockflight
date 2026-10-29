namespace Lockflight.Cli.Ci;

/// <summary>What the interim classifier saw in a change, which decides the requirements beyond its class.</summary>
internal enum InterimShape
{
    /// <summary>Every changed path is on the base's <c>ci.lowRiskPaths</c> allowlist (or nothing changed).</summary>
    LowRisk = 1,

    /// <summary>Only package versions and lock files changed.</summary>
    DependencyOnly = 2,

    /// <summary>Anything else.</summary>
    Other = 3,
}
