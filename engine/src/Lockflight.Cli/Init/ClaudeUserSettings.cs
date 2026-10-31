using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Init;

/// <summary>The user's Claude Code settings (<c>~/.claude/settings.json</c>), read leniently: Lockflight never writes them.</summary>
internal static class ClaudeUserSettings
{
    /// <summary>The project's personal settings, which Claude Code reads before the project's and the user's.</summary>
    public const string LocalRelativePath = ".claude/settings.local.json";

    /// <summary>
    /// Whether the Lockflight plugin is enabled for the project, as Claude Code decides it: <c>.claude/settings.local.json</c>, then
    /// the project's settings, then the user's (<paramref name="userHome"/>; null skips them); the first that sets it wins.
    /// Unreadable settings set nothing.
    /// </summary>
    public static bool PluginEnabledFor(string root, string? userHome) =>
        (PluginSetting(Read(Path.Combine(root, LocalRelativePath)))
            ?? PluginSetting(Read(Path.Combine(root, ClaudeSettingsMerger.RelativePath)))
            ?? (userHome is null ? null : PluginSetting(Read(Path.Combine(userHome, ".claude", "settings.json"))))) == true;

    /// <summary>
    /// Whether the plugin's <c>/lockflight:</c> commands exist for the project at <paramref name="root"/>: false only at the native
    /// level, where the project's settings run the native hooks and its settings (local, then project) do not enable the plugin.
    /// Missing or unreadable settings count as the plugin level, the default.
    /// </summary>
    public static bool PluginCommandsFor(string root) =>
        !NativeHooks.AnyPresent(Read(Path.Combine(root, ClaudeSettingsMerger.RelativePath))) || PluginEnabledFor(root, userHome: null);

    /// <summary>
    /// Whether the project is at the CI-only level (<c>init --ci-only</c>): <c>CLAUDE.md</c> has no Lockflight block, the
    /// project's Claude Code settings are missing or hold nothing Lockflight writes (native hooks, the marketplace, a sandbox
    /// denial or deny rule for a Lockflight path), and no settings enable the plugin. Settings that cannot be read are not taken
    /// for it, so doctor reports them.
    /// </summary>
    public static bool CiOnlyFor(string root, string? userHome)
    {
        if (PluginEnabledFor(root, userHome) || HasClaudeMdBlock(root))
        {
            return false;
        }

        string path = Path.Combine(root, ClaudeSettingsMerger.RelativePath);
        if (!File.Exists(path))
        {
            return true;
        }

        if (ReadText(path) is not string text || ClaudeSettingsMerger.Problem(text, nativeHooks: false) is not null || Parse(text) is not JsonObject settings)
        {
            return false;
        }

        return !NativeHooks.AnyPresent(settings)
            && !(settings["extraKnownMarketplaces"] is JsonObject marketplaces && marketplaces.ContainsKey(Templates.MarketplaceName))
            && !SandboxDenials(settings).Any(NamesLockflight)
            && !PermissionDenyRules.Entries(settings).Any(NamesLockflight);
    }

    /// <summary>The plugin's entry in <c>enabledPlugins</c>: true, false, or null when these settings do not set it.</summary>
    private static bool? PluginSetting(JsonNode? settings) =>
        (settings as JsonObject)?["enabledPlugins"] is JsonObject plugins && plugins[Templates.PluginId] is JsonNode entry
            && entry.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? entry.GetValueKind() == JsonValueKind.True
            : null;

    private static bool HasClaudeMdBlock(string root) =>
        ReadText(Path.Combine(root, ClaudeMdBlock.FileName)) is string text && ClaudeMdBlock.IsPresent(text);

    private static IEnumerable<string> SandboxDenials(JsonObject settings) =>
        settings[SandboxSettings.SandboxKey] is JsonObject sandbox && sandbox[SandboxSettings.FilesystemKey] is JsonObject filesystem
            && filesystem[SandboxSettings.DenyWriteKey] is JsonArray list
            ? list.Where(entry => entry?.GetValueKind() == JsonValueKind.String).Select(entry => entry!.GetValue<string>())
            : [];

    private static bool NamesLockflight(string entry) => entry.Contains("lockflight", StringComparison.OrdinalIgnoreCase);

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static JsonNode? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonNode? Read(string path) => ReadText(path) is string text ? Parse(text) : null;
}
