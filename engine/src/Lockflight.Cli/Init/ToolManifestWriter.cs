using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Init;

/// <summary>Pins the running engine version in the project's <c>dotnet-tools.json</c>, keeping other tools.</summary>
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

    public static (string Json, bool Changed) Pin(string? existingJson)
    {
        JsonObject manifest = existingJson is null ? new JsonObject { ["version"] = 1, ["isRoot"] = true } : JsonNode.Parse(existingJson)!.AsObject();
        if (manifest["tools"] is not JsonObject tools)
        {
            tools = [];
            manifest["tools"] = tools;
        }

        if (tools[PackageId] is JsonObject existing && existing["version"]?.GetValue<string>() is string pinned && ToolPin.Matches(pinned, ToolPin.RunningVersion))
        {
            return (existingJson!, false);
        }

        tools[PackageId] = new JsonObject
        {
            ["version"] = ToolPin.RunningVersion,
            ["commands"] = new JsonArray("lockflight"),
            ["rollForward"] = false,
        };
        return (JsonFiles.Format(manifest), true);
    }
}
