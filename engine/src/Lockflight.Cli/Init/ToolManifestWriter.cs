using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Init;

/// <summary>
/// Pins the running engine version in the project's <c>dotnet-tools.json</c>, keeping other tools. A pin is never lowered: a newer
/// version, or a value that is not a version, is left as it is, and <see cref="Problem"/> says why so <c>init</c> and <c>update</c>
/// can refuse before they write anything. The package id is matched ignoring case, as NuGet does.
/// </summary>
internal static class ToolManifestWriter
{
    private const string PackageId = "lockflight.cli";
    private const string RootManifest = "dotnet-tools.json";
    private const string ConfigManifest = ".config/dotnet-tools.json";

    /// <summary>
    /// Where the pin goes: the repository's existing local tool manifest — <c>dotnet-tools.json</c> at the root (what
    /// <c>dotnet new tool-manifest</c> writes since SDK 10) or an older <c>.config/dotnet-tools.json</c> — else a new root one.
    /// </summary>
    public static string RelativePathIn(string root) =>
        !File.Exists(Path.Combine(root, RootManifest)) && File.Exists(Path.Combine(root, ConfigManifest)) ? ConfigManifest : RootManifest;

    /// <summary>
    /// Why the running engine must not pin itself in this tool manifest — it cannot be read, or it pins a newer engine or something
    /// that is not a version — with what to do; null when <see cref="Pin"/> may write.
    /// </summary>
    public static string? Problem(string? existingJson)
    {
        if (existingJson is null)
        {
            return null;
        }

        JsonObject? tools;
        try
        {
            if (JsonNode.Parse(existingJson) is not JsonObject manifest)
            {
                return "is not a JSON object; fix or remove it";
            }

            tools = manifest["tools"] switch
            {
                null => null,
                JsonObject toolObject => toolObject,
                _ => throw new InvalidOperationException("'tools' is not an object"),
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return $"cannot be read ({exception.Message}); fix or remove it";
        }

        if (tools is null || Entry(tools) is not KeyValuePair<string, JsonNode?> entry)
        {
            return null;
        }

        if (entry.Value is not JsonObject tool)
        {
            return $"has a {entry.Key} entry that is not an object; fix or remove it";
        }

        string running = ToolPin.RunningVersion;
        if (tool["version"] is not JsonValue value || !value.TryGetValue(out string? pinned) || EngineVersion.Parse(pinned) is not EngineVersion version)
        {
            return $"pins {entry.Key} to {tool["version"]?.ToJsonString() ?? "nothing"}, which is not a version; set \"version\" to a version string such as \"{running}\" or remove the entry";
        }

        return EngineVersion.Parse(running) is EngineVersion current && version.CompareTo(current) > 0
            ? $"pins {entry.Key} {pinned}, newer than this engine ({running}); a pin is never lowered. Install Lockflight {pinned} or newer ('lockflight update') and run the command with it"
            : null;
    }

    public static (string Json, bool Changed) Pin(string? existingJson)
    {
        if (existingJson is not null && Problem(existingJson) is not null)
        {
            return (existingJson, false);
        }

        JsonObject manifest = existingJson is null ? new JsonObject { ["version"] = 1, ["isRoot"] = true } : JsonNode.Parse(existingJson)!.AsObject();
        if (manifest["tools"] is not JsonObject tools)
        {
            tools = [];
            manifest["tools"] = tools;
        }

        string key = Entry(tools)?.Key ?? PackageId;
        if (tools[key]?["version"] is JsonValue value && value.TryGetValue(out string? pinned) && ToolPin.Matches(pinned, ToolPin.RunningVersion))
        {
            return (existingJson!, false);
        }

        tools[key] = new JsonObject
        {
            ["version"] = ToolPin.RunningVersion,
            ["commands"] = new JsonArray("lockflight"),
            ["rollForward"] = false,
        };
        return (JsonFiles.Format(manifest), true);
    }

    private static KeyValuePair<string, JsonNode?>? Entry(JsonObject tools) =>
        tools.Where(tool => string.Equals(tool.Key, PackageId, StringComparison.OrdinalIgnoreCase)).Select(tool => (KeyValuePair<string, JsonNode?>?)tool).FirstOrDefault();
}
