using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

/// <summary>Writes a project's <c>.claude/settings.json</c> as <c>init --native</c> does, optionally with the plugin enabled too.</summary>
internal static class NativeLevelSettings
{
    public static void Write(string root, bool pluginEnabled)
    {
        string json = ClaudeSettingsMerger.Merge(null, [], null, InstallLevel.Native).Json;
        if (pluginEnabled)
        {
            json = json.TrimEnd().TrimEnd('}') + $",\n  \"enabledPlugins\": {{ \"{Templates.PluginId}\": true }}\n}}\n";
        }

        Directory.CreateDirectory(Path.Combine(root, ".claude"));
        File.WriteAllText(Path.Combine(root, ClaudeSettingsMerger.RelativePath), json);
    }
}
