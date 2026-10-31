using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// Reads <c>lockflight.engine-hashes/v1</c>, evidence a release writes: unknown properties and platforms are ignored, but the
/// schema, the package, a version, a 40-hex commit and a well-formed hash and path for every released platform are required, a
/// key may appear only once, and the install script's hash, when the file has one, must be well-formed.
/// </summary>
internal static partial class EngineHashesReader
{
    /// <summary>The hashes; null with <paramref name="problem"/> when the file does not hold them.</summary>
    public static EngineHashes? Read(string text, out string? problem)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return Read(document.RootElement, out problem);
        }
        catch (JsonException exception)
        {
            problem = $"not valid JSON: {exception.Message}";
            return null;
        }
    }

    private static EngineHashes? Read(JsonElement root, out string? problem)
    {
        problem = null;
        if (root.ValueKind != JsonValueKind.Object || String(root, "schema") != EngineHashes.Schema
            || !string.Equals(String(root, "package"), EngineLock.Package, StringComparison.OrdinalIgnoreCase))
        {
            problem = $"not a {EngineHashes.Schema} file for {EngineLock.Package}";
            return null;
        }

        if (JsonDuplicateKeys.Find(root) is string duplicate)
        {
            problem = $"the key '{duplicate}' appears twice";
            return null;
        }

        if (String(root, "version") is not string version || String(root, "commit") is not string commit || !Commit().IsMatch(commit))
        {
            problem = "it needs a 'version' and a 40-character 'commit'";
            return null;
        }

        Dictionary<string, EngineBinary> binaries = new(StringComparer.Ordinal);
        if (root.TryGetProperty("binaries", out JsonElement entries) && entries.ValueKind == JsonValueKind.Object)
        {
            foreach (string rid in EngineRids.Released)
            {
                if (entries.TryGetProperty(rid, out JsonElement entry) && String(entry, "sha256") is string hash && Sha256().IsMatch(hash)
                    && String(entry, "path") is string path && path.Length > 0)
                {
                    binaries[rid] = new EngineBinary(hash, path);
                }
            }
        }

        string[] missing = [.. EngineRids.Released.Where(rid => !binaries.ContainsKey(rid))];
        if (missing.Length > 0)
        {
            problem = $"no well-formed hash and path for {string.Join(", ", missing)}";
            return null;
        }

        string? script = null;
        if (root.TryGetProperty("installScript", out JsonElement installScript))
        {
            script = String(installScript, "sha256");
            if (script is null || !Sha256().IsMatch(script))
            {
                problem = "'installScript' must be { \"sha256\": \"<64 lower-case hex>\" }";
                return null;
            }
        }

        return new EngineHashes(version, commit, binaries, script);
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex Commit();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}
