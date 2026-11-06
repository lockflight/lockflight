using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Linking;

namespace Lockflight.Cli.Lifecycle.Fix;

/// <summary>
/// What <c>lockflight red</c> already found, so a later run need not build it again: a fork point's inventory and each red commit's
/// outcome. Derived data in <c>.lockflight/local/cache/red/</c> (git-ignored, and under the guard's protection of
/// <c>.lockflight/</c>): deleting it costs builds, never a different answer. An entry is keyed by the commit, the commit the base
/// policy was read from, the engine's hash and the .NET SDK's version, so a change of any of them misses. A poisoned entry can give
/// a local false accept only: the CI judge keeps no cache and re-runs every red.
/// </summary>
internal sealed class RedCache(string directory, string policyCommit, string engineHash, string sdkVersion)
{
    public const string RelativeDirectory = ".lockflight/local/cache/red";

    private const string ForkKind = "fork";
    private const string RedKind = "red";

    public static string DirectoryOf(string root) => Path.Combine(root, RelativeDirectory.Replace('/', Path.DirectorySeparatorChar));

    public ForkInventory? ReadFork(string commit)
    {
        if (Read(ForkKind, commit) is not JsonObject entry)
        {
            return null;
        }

        return Keys(entry, "tests") is HashSet<string> tests && Keys(entry, "skipped") is HashSet<string> skipped && Keys(entry, "explicit") is HashSet<string> explicitTests
            ? new ForkInventory(tests, skipped, explicitTests)
            : null;
    }

    public void WriteFork(string commit, ForkInventory inventory) => Write(ForkKind, commit, new JsonObject
    {
        ["tests"] = Array(inventory.Tests),
        ["skipped"] = Array(inventory.Skipped),
        ["explicit"] = Array(inventory.Explicit),
    });

    public CachedRed? ReadRed(string commit)
    {
        if (Read(RedKind, commit) is not JsonObject entry || entry["accepted"] is not JsonValue accepted || entry["tests"] is not JsonArray tests)
        {
            return null;
        }

        try
        {
            List<RedGreenTest> read = [.. tests.OfType<JsonObject>().Select(test => new RedGreenTest(
                test["key"]!.GetValue<string>(), test["method"]!.GetValue<string>(), RedClassNames.Parse(test["class"]!.GetValue<string>()), []))];
            return read.Count == tests.Count ? new CachedRed(accepted.GetValue<bool>(), read) : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException or NullReferenceException)
        {
            return null;
        }
    }

    public void WriteRed(string commit, CachedRed red) => Write(RedKind, commit, new JsonObject
    {
        ["accepted"] = red.Accepted,
        ["tests"] = new JsonArray([.. red.Tests.Select(test => (JsonNode)new JsonObject
        {
            ["key"] = test.Key,
            ["method"] = test.MethodFilter,
            ["class"] = RedClassNames.ToName(test.RedClass),
        })]),
    });

    /// <summary>The SHA-256 of the running engine (its executable, or its assembly beside a launcher); null when it cannot be read.</summary>
    public static string? EngineHash(RunningEngine engine)
    {
        string? path = engine.IsSingleFile ? engine.Path : Path.Combine(AppContext.BaseDirectory, typeof(RedCache).Assembly.GetName().Name + ".dll");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private JsonNode? Read(string kind, string commit)
    {
        string path = PathOf(kind, commit);
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void Write(string kind, string commit, JsonObject entry)
    {
        try
        {
            Directory.CreateDirectory(directory);
            JsonFiles.Write(PathOf(kind, commit), entry);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Derived data: a cache that cannot be written costs a build next time, never a different answer.
        }
    }

    private string PathOf(string kind, string commit)
    {
        string key = string.Join('\n', kind, commit, policyCommit, engineHash, sdkVersion);
        return Path.Combine(directory, $"{kind}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32]}.json");
    }

    private static JsonArray Array(IReadOnlySet<string> keys) => new([.. keys.Order(StringComparer.Ordinal).Select(key => (JsonNode)JsonValue.Create(key))]);

    private static HashSet<string>? Keys(JsonObject entry, string name)
    {
        if (entry[name] is not JsonArray array)
        {
            return null;
        }

        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (JsonNode? item in array)
        {
            if (item is not JsonValue value || !value.TryGetValue(out string? key))
            {
                return null;
            }

            keys.Add(key);
        }

        return keys;
    }
}
