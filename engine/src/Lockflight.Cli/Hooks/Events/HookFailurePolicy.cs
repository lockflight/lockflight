namespace Lockflight.Cli.Hooks.Events;

/// <summary>What the hook answers when the engine itself fails while deciding an event.</summary>
internal enum HookFailurePolicy
{
    /// <summary>Exit 2: the tool call or the typed command does not go ahead.</summary>
    Block = 0,

    /// <summary>Exit 0: the error is reported, the session goes on.</summary>
    Allow = 1,
}
