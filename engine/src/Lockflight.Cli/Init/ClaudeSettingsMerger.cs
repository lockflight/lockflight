using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Init;

/// <summary>
/// Adds the Lockflight marketplace and enables the plugin in a project's <c>.claude/settings.json</c>, keeping every other key
/// (a project-scope install alone writes only <c>enabledPlugins</c>, so teammates would never be offered the plugin).
/// </summary>
internal static class ClaudeSettingsMerger
{

    public static (string Json, bool Changed) Merge(string? existingJson)
    {
        JsonObject settings = existingJson is null ? [] : JsonNode.Parse(existingJson)?.AsObject() ?? [];
        bool changed = false;

        JsonObject marketplaces = ObjectAt(settings, "extraKnownMarketplaces");
        if (!marketplaces.ContainsKey(Templates.MarketplaceName))
        {
            marketplaces[Templates.MarketplaceName] = new JsonObject
            {
                ["source"] = new JsonObject { ["source"] = "github", ["repo"] = Templates.MarketplaceRepository },
            };
            changed = true;
        }

        JsonObject enabledPlugins = ObjectAt(settings, "enabledPlugins");
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
