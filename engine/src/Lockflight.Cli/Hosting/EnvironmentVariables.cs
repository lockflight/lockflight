namespace Lockflight.Cli.Hosting;

/// <summary>Environment variables the engine reads.</summary>
internal static class EnvironmentVariables
{
    /// <summary>Path of a file that receives every raw hook payload (for inspecting what Claude Code sends).</summary>
    public const string HookLog = "LOCKFLIGHT_HOOK_LOG";

    /// <summary>The project root where the Claude Code session started; preferred over the session's moving cwd.</summary>
    public const string ClaudeProjectDirectory = "CLAUDE_PROJECT_DIR";

    /// <summary>When set, hooks print path-matching diagnostics to stderr.</summary>
    public const string Debug = "LOCKFLIGHT_DEBUG";

    /// <summary>Path of the gitleaks binary for the secrets gate; when unset, gitleaks is looked up on PATH.</summary>
    public const string Gitleaks = "LOCKFLIGHT_GITLEAKS";

    /// <summary>The hook API version the plugin shim speaks; unset for direct invocations.</summary>
    public const string PluginApi = "LOCKFLIGHT_PLUGIN_API";

    /// <summary>
    /// The oldest engine the plugin works with, read by the shim from the plugin's <c>min-engine-version</c> file at session start;
    /// unset otherwise.
    /// </summary>
    public const string PluginMinimumEngine = "LOCKFLIGHT_PLUGIN_MIN_ENGINE";

    /// <summary>Set to <c>1</c> by Claude Code in every shell the agent starts.</summary>
    public const string ClaudeCode = "CLAUDECODE";

    /// <summary>How Claude Code was started: <c>cli</c> for an interactive session, <c>sdk-cli</c> for a headless one (<c>claude -p</c>).</summary>
    public const string ClaudeCodeEntrypoint = "CLAUDE_CODE_ENTRYPOINT";

    /// <summary><c>1</c> when a person attends the Claude Code session, <c>0</c> for a headless one.</summary>
    public const string ClaudeCodeSessionAttended = "CLAUDE_CODE_SESSION_ATTENDED";

    /// <summary>Any value turns off color, and with it Levi (https://no-color.org).</summary>
    public const string NoColor = "NO_COLOR";

    /// <summary><c>0</c> keeps Levi, the pixel mascot, out of command output.</summary>
    public const string Mascot = "LOCKFLIGHT_MASCOT";

    /// <summary>The directories searched for executables such as <c>claude</c>.</summary>
    public const string Path = "PATH";

    /// <summary>The user's home directory on Unix (and in Git Bash); <c>~</c> in a shell path stands for it.</summary>
    public const string Home = "HOME";

    /// <summary>The user's home directory on Windows, used when <see cref="Home"/> is unset.</summary>
    public const string UserProfile = "USERPROFILE";

    /// <summary>The NuGet global packages folder, where <c>dotnet tool restore</c> caches tools; <c>~/.nuget/packages</c> when unset.</summary>
    public const string NuGetPackages = "NUGET_PACKAGES";
}
