namespace Lockflight.Cli.Gates;

/// <summary>Where a gate step judges in a workspace.</summary>
internal enum StepScope
{
    /// <summary>Once, at the project root (policy, format, secrets).</summary>
    Root = 0,

    /// <summary>In each unit's directory (build, test, coverage, dependencies); in a single repository, at the root.</summary>
    Unit = 1,
}
