using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Doctor;
using Lockflight.Cli.Updating;

namespace Lockflight.Cli.Adoption;

/// <summary>Where the Lockflight Spec Kit preset is on this machine, for the <c>init --speckit-preset</c> line init prints.</summary>
internal static class PresetLocator
{
    private const string PresetDirectory = "speckit-preset";

    /// <summary>
    /// The preset that matches this engine: the installed plugin's (Claude Code's record of installed plugins) when the plugin's
    /// version is the engine's, else the one in the checkout an install from source was built from, else an installed plugin of
    /// another version; null when none is found.
    /// </summary>
    public static string? Find(string? userHome, string root)
    {
        if (userHome is null)
        {
            return null;
        }

        string? plugin = PluginEngineCheck.InstallPath(root, userHome);
        string? pluginPreset = plugin is null ? null : Path.Combine(plugin, PresetDirectory);
        string?[] candidates =
        [
            plugin is not null && PluginVersion(plugin) == ToolPin.RunningVersion ? pluginPreset : null,
            UserInstall.Source(userHome) is string checkout ? Path.Combine(checkout, "plugins", "lockflight", PresetDirectory) : null,
            pluginPreset,
        ];
        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static string? PluginVersion(string plugin)
    {
        try
        {
            string manifest = Path.Combine(plugin, ".claude-plugin", "plugin.json");
            return File.Exists(manifest) && JsonNode.Parse(File.ReadAllText(manifest))?["version"] is JsonValue version
                && version.TryGetValue(out string? text) ? text : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
