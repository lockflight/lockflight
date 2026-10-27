namespace Lockflight.Cli.Hooks;

/// <summary>
/// The exact Bash commands that bring back the pinned engine. They are allowed while the engine is missing (plugin shim) or differs
/// from the pin (engine); anything chained to them is not. The plugin shim keeps the same list.
/// </summary>
internal static class RecoveryCommands
{
    public const string RestoreAndLink = "dotnet tool restore && dotnet lockflight link";

    public static IReadOnlySet<string> Commands { get; } = new HashSet<string>(["dotnet tool restore", RestoreAndLink, "dotnet lockflight link"]);

    public static bool Allows(HookInput input) =>
        input.ToolName == "Bash" && input.Command is string command && Commands.Contains(command.Trim());
}
