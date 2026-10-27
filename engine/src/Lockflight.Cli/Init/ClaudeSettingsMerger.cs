using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Init;

/// <summary>
/// Adds the Lockflight marketplace and enables the plugin in a project's <c>.claude/settings.json</c>, keeping every other key
/// (a project-scope install alone writes only <c>enabledPlugins</c>, so teammates would never be offered the plugin). Settings it
/// cannot merge into without losing something are left as they are, and <see cref="Problem"/> says why.
/// </summary>
internal static class ClaudeSettingsMerger
{
    public const string RelativePath = ".claude/settings.json";

    private const string MarketplacesKey = "extraKnownMarketplaces";
    private const string EnabledPluginsKey = "enabledPlugins";

    /// <summary>
    /// Why the settings cannot be merged into: not strict JSON (comments and trailing commas would be lost when the file is
    /// written back), a key the merge writes into holds something else than an object, or the plugin's entry is not true or
    /// false. Null when <see cref="Merge"/> may write.
    /// </summary>
    public static string? Problem(string? existingJson)
    {
        if (existingJson is null)
        {
            return null;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(existingJson);
        }
        catch (JsonException exception)
        {
            return $"is not valid JSON ({exception.Message})";
        }

        if (root is not JsonObject settings)
        {
            return "is not a JSON object";
        }

        if (new[] { MarketplacesKey, EnabledPluginsKey }.FirstOrDefault(key => settings[key] is not (null or JsonObject)) is string notAnObject)
        {
            return $"has a \"{notAnObject}\" that is not an object";
        }

        return settings[EnabledPluginsKey]?[Templates.PluginId] is JsonNode plugin && plugin.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)
            ? $"has an \"{EnabledPluginsKey}\" entry for {Templates.PluginId} that is not true or false"
            : null;
    }

    public static (string Json, bool Changed) Merge(string? existingJson)
    {
        if (existingJson is not null && Problem(existingJson) is not null)
        {
            return (existingJson, false);
        }

        JsonObject settings = existingJson is null ? [] : JsonNode.Parse(existingJson)?.AsObject() ?? [];
        bool changed = false;

        JsonObject marketplaces = ObjectAt(settings, MarketplacesKey);
        if (!marketplaces.ContainsKey(Templates.MarketplaceName))
        {
            marketplaces[Templates.MarketplaceName] = new JsonObject
            {
                ["source"] = new JsonObject { ["source"] = "github", ["repo"] = Templates.MarketplaceRepository },
            };
            changed = true;
        }

        JsonObject enabledPlugins = ObjectAt(settings, EnabledPluginsKey);
        if (enabledPlugins[Templates.PluginId]?.GetValueKind() != JsonValueKind.True)
        {
            enabledPlugins[Templates.PluginId] = true;
            changed = true;
        }

        return (JsonFiles.Format(settings), changed);
    }

    private static JsonObject ObjectAt(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject existing)
        {
            return existing;
        }

        JsonObject created = [];
        parent[key] = created;
        return created;
    }
}
