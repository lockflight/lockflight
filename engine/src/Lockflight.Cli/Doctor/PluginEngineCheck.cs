using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// Whether the Lockflight plugin works with the project's engine. The plugin updates from its marketplace on its own while the
/// project pins its engine; the plugin names the oldest engine it works with in its <c>min-engine-version</c> file. The copy
/// checked, named in the result, is the first of: the plugin this Claude Code session loaded (its SessionStart hook exports
/// <c>LOCKFLIGHT_PLUGIN_MIN_ENGINE</c> and <c>LOCKFLIGHT_PLUGIN_ROOT</c> to the session, so a plugin loaded with
/// <c>--plugin-dir</c> counts), the plugin at <c>CLAUDE_PLUGIN_ROOT</c>, else the install in Claude Code's record of installed
/// plugins (<c>~/.claude/plugins/installed_plugins.json</c>): the one for this project when there is one, else the user-scope one.
/// </summary>
internal static class PluginEngineCheck
{
    private const string Name = "plugin engine";

    /// <summary>Null when no plugin copy, or no requirement in it, can be found: nothing to compare.</summary>
    public static DoctorCheck? Check(string root, string? userHome, EnvironmentReader environment)
    {
        if (Requirement(root, userHome, environment) is not (string copy, EngineVersion minimum))
        {
            return null;
        }

        string engine = ToolPin.ReadPin(root) is { Status: ToolPinStatus.Pinned, Version: string pinned } ? pinned : ToolPin.RunningVersion;
        return EngineVersion.Parse(engine) is EngineVersion version && version.CompareTo(minimum) < 0
            ? new(Name, CheckStatus.Fail,
                $"{copy} needs engine {minimum} or newer; the project pins {engine}",
                $"a person opens a separate terminal window ({HumanOnlyCommands.NotWithBang}) and runs `{EngineCommand.UserInstall} update` in the project: it installs the newest engine, re-pins the project and links it; then commit dotnet-tools.json")
            : new(Name, CheckStatus.Pass, $"{copy} needs engine {minimum} or newer; the project pins {engine}", null);
    }

    /// <summary>The installed plugin's directory: the install for <paramref name="root"/> when there is one, else the user-scope install; null when none is recorded.</summary>
    public static string? InstallPath(string root, string userHome)
    {
        string record = Path.Combine(userHome, ".claude", "plugins", "installed_plugins.json");
        try
        {
            if (!File.Exists(record) || JsonNode.Parse(File.ReadAllText(record))?["plugins"]?[Templates.PluginId] is not JsonArray installs)
            {
                return null;
            }

            JsonNode? chosen = installs.FirstOrDefault(install => install?["projectPath"]?.GetValue<string>() is string path && SamePath(path, root))
                ?? installs.FirstOrDefault(install => install?["scope"]?.GetValue<string>() == "user");
            return chosen?["installPath"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The plugin copy checked, as the result names it, and the oldest engine it works with; null when none is found.</summary>
    private static (string Copy, EngineVersion Minimum)? Requirement(string root, string? userHome, EnvironmentReader environment)
    {
        if (EngineVersion.Parse(environment.Get(EnvironmentVariables.PluginMinimumEngine) ?? string.Empty) is EngineVersion announced)
        {
            string loaded = environment.Get(EnvironmentVariables.PluginRoot) is { Length: > 0 } pluginRoot ? $" ({pluginRoot})" : string.Empty;
            return ($"the plugin this session loaded{loaded}", announced);
        }

        if (environment.Get(EnvironmentVariables.ClaudePluginRoot) is { Length: > 0 } claudePluginRoot
            && MinimumIn(claudePluginRoot) is EngineVersion atRoot)
        {
            return ($"the plugin at {claudePluginRoot} ({EnvironmentVariables.ClaudePluginRoot})", atRoot);
        }

        return userHome is not null && InstallPath(root, userHome) is string installPath && MinimumIn(installPath) is EngineVersion installed
            ? ($"the installed plugin at {installPath} (Claude Code's install record; a plugin loaded with --plugin-dir is seen only inside its session)", installed)
            : null;
    }

    private static EngineVersion? MinimumIn(string pluginDirectory)
    {
        string requirementPath = Path.Combine(pluginDirectory, PluginHandshake.MinimumEngineFile);
        return File.Exists(requirementPath) ? EngineVersion.Parse(File.ReadAllText(requirementPath).Trim()) : null;
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
