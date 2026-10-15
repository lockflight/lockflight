namespace Lockflight.Cli.Hooks;

/// <summary>The Claude Code hook events the engine is invoked for (<c>lockflight hook &lt;event&gt;</c>).</summary>
internal enum HookEvent
{
    /// <summary>An event name the engine does not know; it is logged and allowed.</summary>
    Unknown = 0,

    /// <summary>Before a tool call runs — the protected-path guard decides whether it may.</summary>
    PreToolUse = 1,

    /// <summary>After a tool call finished — logged only.</summary>
    PostToolUse = 2,

    /// <summary>The agent wants to end its turn — the stop gate is the in-session definition of done.</summary>
    Stop = 3,

    /// <summary>A subagent wants to finish — logged only.</summary>
    SubagentStop = 4,

    /// <summary>A session starts — the engine injects a short project banner.</summary>
    SessionStart = 5,

    /// <summary>The person's typed prompt expands a slash command — <c>/lockflight:approve</c> records an approval in the session.</summary>
    UserPromptExpansion = 6,
}
