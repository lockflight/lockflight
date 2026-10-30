using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lockflight.Cli.Init;

/// <summary>The user's Claude Code settings (<c>~/.claude/settings.json</c>), read leniently: Lockflight never writes them.</summary>
internal static class ClaudeUserSettings
{
    /// <summary>Whether the project's or the user's settings enable the Lockflight plugin; unreadable settings enable nothing.</summary>
    public static bool PluginEnabledFor(string root, string? userHome) =>
        ClaudeSettingsMerger.PluginEnabled(Read(Path.Combine(root, ClaudeSettingsMerger.RelativePath)))
        || (userHome is not null && ClaudeSettingsMerger.PluginEnabled(Read(Path.Combine(userHome, ".claude", "settings.json"))));

    private static JsonNode? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return null;
        }
    }
}
