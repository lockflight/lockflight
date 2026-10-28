using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// The Lockflight preset in Spec Kit's preset registry: <c>specify preset add</c> records installed presets in
/// <c>.specify/presets/.registry</c> (JSON: <c>presets.&lt;id&gt;</c>, with <c>enabled</c>).
/// </summary>
internal static class LockflightPresetRegistry
{
    private const string PresetId = "lockflight";

    /// <summary>The Lockflight entry in the registry; null when there is none.</summary>
    /// <exception cref="JsonException">The registry is not JSON.</exception>
    public static JsonNode? Entry(string root)
    {
        string registryPath = Path.Combine(root, ".specify", "presets", ".registry");
        return File.Exists(registryPath) ? JsonNode.Parse(File.ReadAllText(registryPath))?["presets"]?[PresetId] : null;
    }

    /// <summary>Whether an entry is an enabled preset: an object whose <c>enabled</c> is not <c>false</c>.</summary>
    public static bool IsEnabled(JsonNode? entry) => entry is JsonObject && entry["enabled"]?.GetValueKind() != JsonValueKind.False;

    /// <summary>Whether the preset is installed and enabled; false too when the registry cannot be read.</summary>
    public static bool IsInstalled(string root)
    {
        try
        {
            return IsEnabled(Entry(root));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}
