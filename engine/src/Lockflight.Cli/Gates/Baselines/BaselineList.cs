using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Gates.Baselines;

/// <summary>A baseline file holding a sorted, distinct list of strings under one key, tagged with its schema.</summary>
internal sealed class BaselineList(string path, string schema, string key)
{
    public IReadOnlySet<string> Load() =>
        File.Exists(path) && JsonNode.Parse(File.ReadAllText(path))?[key] is JsonArray entries
            ? entries.Select(entry => entry?.GetValue<string>() ?? string.Empty).Where(entry => entry.Length > 0).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    public void Save(IEnumerable<string> values)
    {
        // Added one by one through the non-generic Add(JsonNode): the generic overload a collection expression picks is not AOT-safe.
        JsonArray entries = [];
        foreach (string value in values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            entries.Add((JsonNode)value);
        }

        JsonFiles.Write(path, new JsonObject { ["schema"] = schema, [key] = entries });
    }
}
