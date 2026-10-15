namespace Lockflight.Cli.Hosting;

/// <summary>
/// The engine's process exit codes — the contract every caller relies on: Claude Code hooks, CI jobs and scripts.
/// </summary>
internal enum ExitCode
{
    /// <summary>The command succeeded; for a gate, the verdict is PASS.</summary>
    Pass = 0,

    /// <summary>A gate ran and found a problem (verdict FAIL).</summary>
    Fail = 1,

    /// <summary>Block the action. Claude Code treats only exit 2 as a blocking hook result.</summary>
    HookBlock = 2,

    /// <summary>The engine or a tool it ran broke; the result is not a verdict about the code.</summary>
    Error = 3,

    /// <summary>Invalid command line, configuration or target.</summary>
    Config = 4,

    /// <summary>A gate ran but had nothing to judge (verdict NOT-EARNED, e.g. zero tests).</summary>
    NotEarned = 5,
}
