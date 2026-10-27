using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Init;

/// <summary>
/// Adds the Lockflight marketplace, enables the plugin and adds the <c>Edit</c> deny rules (<see cref="PermissionDenyRules"/>)
/// and the sandbox write denials (<see cref="SandboxSettings"/>) for the protected paths in a project's <c>.claude/settings.json</c>, keeping every other key
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
    /// written back), a key the merge writes into holds something else than an object (or a list of paths), or the plugin's entry
    /// is not true or false. Null when <see cref="Merge"/> may write.
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

        if ((SandboxSettings.Problem(settings) ?? PermissionDenyRules.Problem(settings)) is string listProblem)
        {
            return listProblem;
        }

        return settings[EnabledPluginsKey]?[Templates.PluginId] is JsonNode plugin && plugin.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)
            ? $"has an \"{EnabledPluginsKey}\" entry for {Templates.PluginId} that is not true or false"
            : null;
    }

    /// <param name="existingJson">The settings file's text; null when there is none.</param>
    /// <param name="denyRules">The <c>permissions.deny</c> rules to add (<see cref="PermissionDenyRules.For"/>).</param>
    /// <param name="denyWrite">The sandbox write denials to add (<see cref="SandboxSettings.DenyWrite"/>); null adds none.</param>
    public static (string Json, bool Changed) Merge(string? existingJson, IReadOnlyList<string> denyRules, IReadOnlyList<string>? denyWrite)
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

        changed |= PermissionDenyRules.Merge(settings, denyRules);
        if (denyWrite is not null)
        {
            changed |= SandboxSettings.Merge(settings, denyWrite);
        }

        return (JsonFiles.Format(settings), changed);
    }

    /// <summary>
    /// Only the protected-path lists, for <c>adopt apply</c>, which leaves the plugin settings to <c>init</c>: the deny rules, and the
    /// sandbox write denials unless null. A file that does not exist is created; one that cannot be merged into is left as it is.
    /// </summary>
    public static (string Json, bool Changed) MergeProtectedPaths(string? existingJson, IReadOnlyList<string> denyRules, IReadOnlyList<string>? denyWrite)
    {
        if (existingJson is not null && Problem(existingJson) is not null)
        {
            return (existingJson, false);
        }

        JsonObject settings = existingJson is null ? [] : JsonNode.Parse(existingJson)?.AsObject() ?? [];
        bool changed = PermissionDenyRules.Merge(settings, denyRules);
        if (denyWrite is not null)
        {
            changed |= SandboxSettings.Merge(settings, denyWrite);
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
