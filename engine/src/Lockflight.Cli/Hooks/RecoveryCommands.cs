using Lockflight.Cli.Hooks.Requests;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// The exact POSIX shell commands that bring back the pinned engine. They are allowed while the engine is missing (plugin shim) or differs
/// from the pin (engine); anything chained to them is not. The plugin shim keeps the same list.
/// </summary>
internal static class RecoveryCommands
{
    public const string RestoreAndLink = "dotnet tool restore && dotnet lockflight link";

    /// <summary>Links the locked engine already installed for the user, after a branch switch moved the lock; it installs nothing.</summary>
    public const string LinkInstalled = "~/.lockflight/bin/lockflight link --installed";

    /// <remarks>
    /// The project's own <c>.lockflight/bin/lockflight link --installed</c> is not among them: the shim allows these while the linked
    /// engine is refused, and that command would run the very engine it refused. The user's install (<c>~/.lockflight</c>) is
    /// guarded against the agent's writes instead (<see cref="Guard.UserInstallRule"/>).
    /// </remarks>
    public static IReadOnlySet<string> Commands { get; } = new HashSet<string>(["dotnet tool restore", RestoreAndLink, "dotnet lockflight link", LinkInstalled]);

    public static bool Allows(HookRequest input) =>
        input.Tool is { Kind: ToolKind.Shell, Shell: ShellDialect.Posix } && input.Command is string command && Commands.Contains(command.Trim());
}
