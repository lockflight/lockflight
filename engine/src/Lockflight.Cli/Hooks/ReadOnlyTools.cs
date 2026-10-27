namespace Lockflight.Cli.Hooks;

/// <summary>
/// The Claude Code tools that only read. Lockdown, an invalid manifest and an engine that differs from the pin allow these and
/// nothing else (plus <see cref="RecoveryCommands"/> where the fix is a restore). The plugin shim keeps the same list.
/// </summary>
internal static class ReadOnlyTools
{
    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(["Read", "Grep", "Glob", "LS"]);

    public static bool Allows(HookInput input) => input.ToolName is string toolName && Names.Contains(toolName);
}
