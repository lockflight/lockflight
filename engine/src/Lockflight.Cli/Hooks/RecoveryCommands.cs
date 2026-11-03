using System.Text.RegularExpressions;
using Lockflight.Cli.Hooks.Requests;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// The exact POSIX shell commands that bring back the pinned engine. They are allowed while the engine is missing (plugin shim) or
/// differs from the pin (engine); anything chained to them is not. The plugin shim keeps the same list.
/// </summary>
/// <remarks>
/// The user's install is named by the absolute path under the hook's own <c>HOME</c>, never <c>~</c>: the Bash tool's shell may
/// have sourced an <c>export HOME=…</c>, and <c>~</c> would then run a program of the agent's under that name. The dotnet commands
/// stay a guardrail: <c>dotnet</c> reads <c>PATH</c>, <c>DOTNET_*</c> and <c>NUGET_PACKAGES</c> from that shell too.
/// </remarks>
internal static partial class RecoveryCommands
{
    public const string RestoreAndLink = "dotnet tool restore && dotnet lockflight link";

    /// <summary>The commands that need no home: restoring and linking the project's pinned tool.</summary>
    public static IReadOnlyList<string> DotnetCommands { get; } = ["dotnet tool restore", RestoreAndLink, "dotnet lockflight link"];

    /// <summary>
    /// Links the locked engine already installed for the user, after a branch switch moved the lock; it installs nothing. Null
    /// without a <c>HOME</c>, or with one a single-quoted word cannot hold. The project's own
    /// <c>.lockflight/bin/lockflight link --installed</c> is never allowed: the shim allows these while the linked engine is refused,
    /// and that command would run the very engine it refused. The user's install is guarded against the agent's writes instead
    /// (<see cref="Guard.UserInstallRule"/>).
    /// </summary>
    public static string? LinkInstalled(string? home)
    {
        if (string.IsNullOrEmpty(home) || home.Contains('\'', StringComparison.Ordinal))
        {
            return null;
        }

        string program = $"{home.TrimEnd('/', '\\')}/.lockflight/bin/lockflight";
        return $"{(PlainWord().IsMatch(program) ? program : $"'{program}'")} link --installed";
    }

    /// <summary>Every command allowed with this <c>HOME</c>.</summary>
    public static IReadOnlySet<string> Commands(string? home) =>
        new HashSet<string>(LinkInstalled(home) is string linkInstalled ? [.. DotnetCommands, linkInstalled] : DotnetCommands, StringComparer.Ordinal);

    public static bool Allows(HookRequest input, string? home) =>
        input.Tool is { Kind: ToolKind.Shell, Shell: ShellDialect.Posix } && input.Command is string command && Commands(home).Contains(command.Trim());

    /// <summary>A word the shell takes as written; the shim quotes by the same rule.</summary>
    [GeneratedRegex("^[A-Za-z0-9_./:-]+$")]
    private static partial Regex PlainWord();
}
