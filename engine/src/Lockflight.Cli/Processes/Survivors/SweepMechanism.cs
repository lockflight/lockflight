namespace Lockflight.Cli.Processes.Survivors;

/// <summary>How the engine finds the processes its children left running.</summary>
internal enum SweepMechanism
{
    /// <summary>It does not: the platform has no way the engine uses yet, or setting it up failed (the report's note says which).</summary>
    None = 0,

    /// <summary>Linux: the engine is a child subreaper, so an orphaned descendant is re-parented to it and found by walking <c>/proc</c>.</summary>
    Subreaper = 1,

    /// <summary>Windows: the engine runs in a job object, which every process it or its descendants create joins.</summary>
    Job = 2,
}
