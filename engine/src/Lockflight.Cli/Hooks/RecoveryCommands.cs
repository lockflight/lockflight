namespace Lockflight.Cli.Hooks;

/// <summary>
/// The exact Bash commands that bring back the pinned engine. They are allowed while the engine is missing (plugin shim) or differs
/// from the pin (engine); anything chained to them is not. The plugin shim keeps the same list.
/// </summary>
internal static class RecoveryCommands
{
    public const string RestoreAndLink = "dotnet tool restore && dotnet lockflight link";

    private static readonly HashSet<string> Allowed = ["dotnet tool restore", RestoreAndLink, "dotnet lockflight link"];

    public static bool Allows(HookInput input) =>
        input.ToolName == "Bash" && input.Command is string command && Allowed.Contains(command.Trim());
}
