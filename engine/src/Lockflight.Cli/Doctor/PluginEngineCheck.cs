using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// Whether the installed Lockflight plugin works with the project's engine. The plugin updates from its marketplace on its own
/// while the project pins its engine; the plugin names the oldest engine it works with in its <c>min-engine-version</c> file.
/// The plugin is found through Claude Code's record of installed plugins (<c>~/.claude/plugins/installed_plugins.json</c>): the
/// install for this project when there is one, else the user-scope install.
/// </summary>
internal static class PluginEngineCheck
{
    private const string Name = "plugin engine";

    /// <summary>Null when the plugin, its install record or its requirement cannot be found: nothing to compare.</summary>
    public static DoctorCheck? Check(string root, string? userHome)
    {
        if (userHome is null || InstallPath(root, userHome) is not string installPath)
        {
            return null;
        }

        string requirementPath = Path.Combine(installPath, PluginHandshake.MinimumEngineFile);
        string? minimumText = File.Exists(requirementPath) ? File.ReadAllText(requirementPath).Trim() : null;
        if (minimumText is null || EngineVersion.Parse(minimumText) is not EngineVersion minimum)
        {
            return null;
        }

        string engine = ToolPin.ReadPin(root) is { Status: ToolPinStatus.Pinned, Version: string pinned } ? pinned : ToolPin.RunningVersion;
        return EngineVersion.Parse(engine) is EngineVersion version && version.CompareTo(minimum) < 0
            ? new(Name, CheckStatus.Fail,
                $"the installed plugin needs engine {minimum} or newer; the project pins {engine}",
                $"a person opens a separate terminal window ({HumanOnlyCommands.NotWithBang}) and runs `{EngineCommand.UserInstall} update` in the project: it installs the newest engine, re-pins the project and links it; then commit dotnet-tools.json")
            : new(Name, CheckStatus.Pass, $"the plugin needs engine {minimum} or newer; the project pins {engine}", null);
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

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
