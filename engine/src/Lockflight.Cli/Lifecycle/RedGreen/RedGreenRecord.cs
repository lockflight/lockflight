using System.Globalization;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// The red/green evidence of a feature in <c>.lockflight/features/&lt;id&gt;/redgreen.json</c> (protected; engine-written): the test
/// inventory when implementation started (write-once, with the commit), the test project directories, and the cycles with the
/// amends a person gave in them.
/// </summary>
internal sealed record RedGreenRecord
{
    public const string FileName = "redgreen.json";

    private const string Schema = "lockflight.redgreen/v1";

    public required string BaselineCommit { get; init; }

    public required DateTimeOffset BaselineAt { get; init; }

    public required IReadOnlySet<string> BaselineTests { get; init; }

    /// <summary>Tests already skipped when the baseline was taken; any other skipped test fails a green.</summary>
    public IReadOnlySet<string> BaselineSkipped { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Tests already marked <c>Explicit = true</c> when the baseline was taken; any other explicit test fails a red or a green. A record
    /// written before this field existed reads as "none were explicit" (strict).
    /// </summary>
    public IReadOnlySet<string> BaselineExplicit { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Test project directories relative to the root, '/'-separated; locked while a cycle is open.</summary>
    public required IReadOnlyList<string> TestProjects { get; init; }

    public List<RedGreenCycle> Cycles { get; init; } = [];

    public RedGreenCycle? OpenCycle => Cycles.LastOrDefault(cycle => cycle.IsOpen);

    /// <summary>Every amend a person gave, in cycle order.</summary>
    public IEnumerable<RedGreenAmend> Amends => Cycles.SelectMany(cycle => cycle.Amends);

    /// <summary>Tests that are not new any more: the baseline plus every earlier cycle's tests.</summary>
    public IReadOnlySet<string> KnownTests => BaselineTests.Concat(Cycles.SelectMany(cycle => cycle.Tests).Select(test => test.Key)).ToHashSet(StringComparer.Ordinal);

    public static string PathFor(Feature feature) => Path.Combine(feature.RecordDirectory, FileName);

    public static RedGreenRecord? Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : null;

    /// <summary>The record in <paramref name="text"/>; null when it holds no baseline. Throws like <see cref="Load"/> when it is not a record.</summary>
    public static RedGreenRecord? Parse(string text)
    {
        if (JsonNode.Parse(text) is not JsonObject root || root["baseline"] is not JsonObject baseline)
        {
            return null;
        }

        return new RedGreenRecord
        {
            BaselineCommit = baseline["commit"]?.GetValue<string>() ?? string.Empty,
            BaselineAt = DateTimeOffset.Parse(baseline["at"]?.GetValue<string>() ?? "1970-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            BaselineTests = Strings(baseline["tests"]).ToHashSet(StringComparer.Ordinal),
            BaselineSkipped = Strings(baseline["skipped"]).ToHashSet(StringComparer.Ordinal),
            BaselineExplicit = Strings(baseline["explicit"]).ToHashSet(StringComparer.Ordinal),
            TestProjects = Strings(root["testProjects"]),
            Cycles = [.. (root["cycles"] as JsonArray ?? []).OfType<JsonObject>().Select(ReadCycle)],
        };
    }

    public void Save(string path, string featureId)
    {
        JsonArray cycles = [];
        foreach (RedGreenCycle cycle in Cycles)
        {
            JsonArray tests = [];
            foreach (RedGreenTest test in cycle.Tests)
            {
                JsonObject entry = new()
                {
                    ["key"] = test.Key,
                    ["method"] = test.MethodFilter,
                    ["red"] = RedClassNames.ToName(test.RedClass),
                    ["requirements"] = Array(test.Requirements),
                };
                if (test.AssertionFree)
                {
                    entry["assertionFree"] = true;
                }

                if (test.Rows > 0)
                {
                    entry["rows"] = test.Rows;
                }

                tests.Add((JsonNode)entry);
            }

            JsonObject saved = new()
            {
                ["number"] = cycle.Number,
                ["redAt"] = cycle.RedAt.ToString("O", CultureInfo.InvariantCulture),
                ["greenAt"] = cycle.GreenAt?.ToString("O", CultureInfo.InvariantCulture),
                ["tests"] = tests,
            };
            if (cycle.Snapshot is string snapshot)
            {
                saved["snapshot"] = snapshot;
            }

            if (cycle.Amends.Count > 0)
            {
                saved["amends"] = AmendsArray(cycle.Amends);
            }

            cycles.Add((JsonNode)saved);
        }

        JsonObject root = new()
        {
            ["schema"] = Schema,
            ["feature"] = featureId,
            ["baseline"] = new JsonObject
            {
                ["commit"] = BaselineCommit,
                ["at"] = BaselineAt.ToString("O", CultureInfo.InvariantCulture),
                ["tests"] = Array(BaselineTests.Order(StringComparer.Ordinal)),
                ["skipped"] = Array(BaselineSkipped.Order(StringComparer.Ordinal)),
                ["explicit"] = Array(BaselineExplicit.Order(StringComparer.Ordinal)),
            },
            ["testProjects"] = Array(TestProjects),
            ["cycles"] = cycles,
        };
        JsonFiles.Write(path, root);
    }

    private static JsonArray AmendsArray(IReadOnlyList<RedGreenAmend> amends)
    {
        JsonArray saved = [];
        foreach (RedGreenAmend amend in amends)
        {
            saved.Add((JsonNode)new JsonObject
            {
                ["test"] = amend.TestKey,
                ["method"] = amend.MethodFilter,
                ["files"] = Array(amend.Files),
                ["by"] = amend.By,
                ["at"] = amend.At.ToString("O", CultureInfo.InvariantCulture),
                ["reason"] = amend.Reason,
                ["recheckedAt"] = amend.RecheckedAt?.ToString("O", CultureInfo.InvariantCulture),
            });
        }

        return saved;
    }

    private static RedGreenAmend ReadAmend(JsonObject amend) => new(
        amend["test"]?.GetValue<string>() ?? string.Empty,
        amend["method"]?.GetValue<string>() ?? string.Empty,
        Strings(amend["files"]),
        amend["by"]?.GetValue<string>() ?? string.Empty,
        DateTimeOffset.Parse(amend["at"]?.GetValue<string>() ?? "1970-01-01T00:00:00Z", CultureInfo.InvariantCulture),
        amend["reason"]?.GetValue<string>() ?? string.Empty)
    {
        RecheckedAt = amend["recheckedAt"]?.GetValue<string>() is string rechecked ? DateTimeOffset.Parse(rechecked, CultureInfo.InvariantCulture) : null,
    };

    private static RedGreenCycle ReadCycle(JsonObject cycle) => new(
        cycle["number"]?.GetValue<int>() ?? 0,
        DateTimeOffset.Parse(cycle["redAt"]?.GetValue<string>() ?? "1970-01-01T00:00:00Z", CultureInfo.InvariantCulture),
        cycle["greenAt"]?.GetValue<string>() is string greenAt ? DateTimeOffset.Parse(greenAt, CultureInfo.InvariantCulture) : null,
        [.. (cycle["tests"] as JsonArray ?? []).OfType<JsonObject>().Select(test => new RedGreenTest(
            test["key"]?.GetValue<string>() ?? string.Empty,
            test["method"]?.GetValue<string>() ?? string.Empty,
            RedClassNames.Parse(test["red"]?.GetValue<string>() ?? "RED_STUB"),
            Strings(test["requirements"]))
        {
            AssertionFree = test["assertionFree"]?.GetValue<bool>() == true,
            Rows = test["rows"]?.GetValue<int>() ?? 0,
        })])
    {
        Amends = [.. (cycle["amends"] as JsonArray ?? []).OfType<JsonObject>().Select(ReadAmend)],
        Snapshot = cycle["snapshot"]?.GetValue<string>(),
    };

    private static string[] Strings(JsonNode? node) =>
        node is JsonArray array ? [.. array.Select(item => item?.GetValue<string>() ?? string.Empty).Where(item => item.Length > 0)] : [];

    private static JsonArray Array(IEnumerable<string> values)
    {
        // Non-generic Add(JsonNode): the generic overload is not AOT-safe.
        JsonArray array = [];
        foreach (string value in values)
        {
            array.Add((JsonNode)value);
        }

        return array;
    }
}
