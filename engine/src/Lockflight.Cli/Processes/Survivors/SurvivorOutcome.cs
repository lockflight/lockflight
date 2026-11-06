namespace Lockflight.Cli.Processes.Survivors;

/// <summary>What became of a process found still running when the engine had no child running.</summary>
internal enum SurvivorOutcome
{
    /// <summary>The engine killed it and saw it gone.</summary>
    Stopped = 0,

    /// <summary>It had already exited on its own and only waited to be reaped; the engine reaped it.</summary>
    Exited = 1,

    /// <summary>It was still running after the engine's attempts to kill it (another user's process, typically started through sudo).</summary>
    NotStopped = 2,

    /// <summary>Found and left running: a local run lists the Docker containers created while it ran but does not remove them.</summary>
    Observed = 3,
}
