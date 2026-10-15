namespace Lockflight.Cli.Hooks;

/// <summary>Command-line spellings of <see cref="HookEvent"/> as used in the plugin's hooks.json.</summary>
internal static class HookEventNames
{
    public static HookEvent Parse(string name) => name switch
    {
        "pre-tool-use" => HookEvent.PreToolUse,
        "post-tool-use" => HookEvent.PostToolUse,
        "stop" => HookEvent.Stop,
        "subagent-stop" => HookEvent.SubagentStop,
        "session-start" => HookEvent.SessionStart,
        "user-prompt-expansion" => HookEvent.UserPromptExpansion,
        _ => HookEvent.Unknown,
    };

    public static string ToName(HookEvent hookEvent) => hookEvent switch
    {
        HookEvent.PreToolUse => "pre-tool-use",
        HookEvent.PostToolUse => "post-tool-use",
        HookEvent.Stop => "stop",
        HookEvent.SubagentStop => "subagent-stop",
        HookEvent.SessionStart => "session-start",
        HookEvent.UserPromptExpansion => "user-prompt-expansion",
        _ => "unknown",
    };
}
