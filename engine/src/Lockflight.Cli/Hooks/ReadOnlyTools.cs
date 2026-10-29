using Lockflight.Cli.Hooks.Requests;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// The tool calls that only read (<see cref="ToolKind.Read"/>, <see cref="ToolKind.Search"/>). Lockdown, an invalid manifest and
/// an engine that differs from the pin allow these and nothing else (plus <see cref="RecoveryCommands"/> where the fix is a
/// restore). The plugin shim keeps Claude Code's names of the same tools.
/// </summary>
internal static class ReadOnlyTools
{
    public static bool Allows(HookRequest input) => input.Tool.OnlyReads;
}
