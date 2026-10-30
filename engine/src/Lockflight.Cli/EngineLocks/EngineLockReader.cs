using System.Text.Json;
using System.Text.RegularExpressions;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// The strict reader of <c>lockflight.engine-lock/v1</c>: a lock is policy a person commits, so an unknown key, a wrong type, a hash
/// that is not 64 lower-case hex, a release lock without every released platform, or a development lock where only release
/// locks belong is an error naming the problem. A RID this engine does not know is ignored, so a later platform does not break
/// an older engine.
/// </summary>
internal static partial class EngineLockReader
{
    private const string Known = "schema, package, version, channel, installScript, binaries";

    /// <param name="allowDevelopment">Whether a <c>development</c> lock is acceptable (the git-ignored local lock only).</param>
    /// <param name="engine">The engine reading the lock, named when a key is unknown.</param>
    public static EngineLockReading Read(string text, bool allowDevelopment, string engine)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return Read(document.RootElement, allowDevelopment, engine);
        }
        catch (JsonException exception)
        {
            return Problem($"not valid JSON: {exception.Message}");
        }
    }

    /// <summary>The lock at <paramref name="path"/>; a missing file reads as no lock and no problem.</summary>
    public static EngineLockReading ReadFile(string path, bool allowDevelopment, string engine)
    {
        if (!File.Exists(path))
        {
            return new EngineLockReading(null, null);
        }

        EngineLockReading reading = Read(File.ReadAllText(path), allowDevelopment, engine);
        return reading.Problem is string problem ? Problem($"{path}: {problem}") : reading;
    }

    private static EngineLockReading Read(JsonElement root, bool allowDevelopment, string engine)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Problem("the lock must be a JSON object");
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!Known.Split(", ").Contains(property.Name, StringComparer.Ordinal))
            {
                return Problem($"unknown key '{property.Name}' (known: {Known}) — this engine is {engine}; a newer engine may know it");
            }
        }

        if (String(root, "schema") != EngineLock.Schema)
        {
            return Problem($"'schema' must be {EngineLock.Schema}");
        }

        if (!string.Equals(String(root, "package"), EngineLock.Package, StringComparison.OrdinalIgnoreCase))
        {
            return Problem($"'package' must be {EngineLock.Package}");
        }

        string? version = String(root, "version");
        if (version is null || version.Contains('+', StringComparison.Ordinal) || EngineVersion.Parse(version) is null)
        {
            return Problem("'version' must be an engine version without build metadata, such as 0.3.0");
        }

        if (EngineLockNames.ChannelOf(String(root, "channel")) is not EngineLockChannel channel)
        {
            return Problem("'channel' must be release or development");
        }

        if (channel == EngineLockChannel.Development && !allowDevelopment)
        {
            return Problem($"a development lock belongs only in the git-ignored {EngineLock.LocalRelativePath}; a committed lock is a release");
        }

        if (HashOf(root, "installScript", out string? scriptProblem) is not string scriptHash)
        {
            return Problem(scriptProblem!);
        }

        if (!root.TryGetProperty("binaries", out JsonElement binaries) || binaries.ValueKind != JsonValueKind.Object)
        {
            return Problem("'binaries' must map each platform to { \"sha256\": … }");
        }

        Dictionary<string, string> hashes = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in binaries.EnumerateObject())
        {
            if (!EngineRids.Released.Contains(entry.Name, StringComparer.Ordinal))
            {
                continue;
            }

            if (HashOf(binaries, entry.Name, out string? entryProblem) is not string hash)
            {
                return Problem(entryProblem!);
            }

            hashes[entry.Name] = hash;
        }

        string[] missing = [.. EngineRids.Released.Where(rid => !hashes.ContainsKey(rid))];
        if (channel == EngineLockChannel.Release && missing.Length > 0)
        {
            return Problem($"a release lock needs a hash for every released platform; missing: {string.Join(", ", missing)}");
        }

        return hashes.Count == 0
            ? Problem("'binaries' holds no hash of a platform this engine knows")
            : new EngineLockReading(new EngineLock(version, channel, scriptHash, hashes), null);
    }

    /// <summary>The hash of an entry <c>{ "sha256": "…" }</c>; null with the problem when it is not exactly that.</summary>
    private static string? HashOf(JsonElement parent, string key, out string? problem)
    {
        problem = $"'{key}' must be {{ \"sha256\": \"<64 lower-case hex>\" }}";
        if (!parent.TryGetProperty(key, out JsonElement entry) || entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (JsonProperty property in entry.EnumerateObject())
        {
            if (property.Name != "sha256")
            {
                problem = $"unknown key '{key}.{property.Name}' (known: sha256)";
                return null;
            }
        }

        string? hash = String(entry, "sha256");
        if (hash is null || !Sha256().IsMatch(hash))
        {
            return null;
        }

        problem = null;
        return hash;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static EngineLockReading Problem(string problem) => new(null, problem);

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}
