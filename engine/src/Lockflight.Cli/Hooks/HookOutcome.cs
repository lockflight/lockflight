namespace Lockflight.Cli.Hooks;

/// <summary>What a hook tells Claude Code to do with the action it was asked about.</summary>
internal enum HookOutcome
{
    /// <summary>Let the action proceed; the engine prints nothing.</summary>
    Allow = 0,

    /// <summary>Stop the action: exit 2 with the reason on stderr (blocks regardless of stdout parsing).</summary>
    Block = 1,
}
