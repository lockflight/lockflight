using System.Text.Json.Nodes;

namespace Lockflight.Cli.Tests.Schemas;

/// <summary>
/// Compares a released JSON Schema with the current one, node by node through <c>properties</c>, <c>$defs</c>, <c>items</c>,
/// <c>additionalProperties</c> and <c>anyOf</c>, <c>allOf</c> and <c>oneOf</c> by position: what the current one changed that is not an addition. A property may be added
/// and a constraint loosened; a property removed, made required, given fewer types or enum values, a different <c>$ref</c>, a
/// <c>const</c> the current one no longer accepts, or closed to unknown properties is a breaking change.
/// </summary>
internal static class SchemaAdditivity
{
    private static readonly string[] Maps = ["properties", "$defs", "patternProperties"];
    private static readonly string[] Lists = ["anyOf", "allOf", "oneOf"];
    private static readonly string[] Nodes = ["items", "additionalProperties"];

    public static IReadOnlyList<string> Breaks(JsonNode released, JsonNode current)
    {
        List<string> breaks = [];
        Compare(released, current, "#", breaks);
        return breaks;
    }

    private static void Compare(JsonNode? released, JsonNode? current, string path, List<string> breaks)
    {
        if (released is not JsonObject old)
        {
            if (released is JsonValue open && open.GetValue<bool>() && current is JsonValue shut && !shut.GetValue<bool>())
            {
                breaks.Add($"{path}: was true, is false");
            }

            return;
        }

        if (current is not JsonObject now)
        {
            breaks.Add($"{path}: is gone");
            return;
        }

        string[] newlyRequired = [.. Strings(now["required"]).Except(Strings(old["required"]), StringComparer.Ordinal)];
        if (newlyRequired.Length > 0)
        {
            breaks.Add($"{path}: newly required {string.Join(", ", newlyRequired)}");
        }

        foreach (string keyword in new[] { "type", "enum" })
        {
            string[] lost = old[keyword] is null ? [] : [.. Strings(old[keyword]).Except(now[keyword] is null ? Strings(old[keyword]) : Strings(now[keyword]), StringComparer.Ordinal)];
            if (lost.Length > 0)
            {
                breaks.Add($"{path}: {keyword} lost {string.Join(", ", lost)}");
            }
        }

        if (old["$ref"] is JsonNode reference && now["$ref"]?.ToJsonString() != reference.ToJsonString())
        {
            breaks.Add($"{path}: $ref changed from {reference.ToJsonString()}");
        }

        // A const that became an enum holding it gained values, which is an addition.
        if (old["const"] is JsonNode constant && now["const"]?.ToJsonString() != constant.ToJsonString()
            && !Strings(now["enum"]).Contains(constant.ToJsonString(), StringComparer.Ordinal))
        {
            breaks.Add($"{path}: const changed from {constant.ToJsonString()}");
        }

        if (now["additionalProperties"] is JsonValue closed && !closed.GetValue<bool>() && old["additionalProperties"] is not JsonValue)
        {
            breaks.Add($"{path}: closed to unknown properties");
        }

        foreach (string map in Maps)
        {
            foreach ((string name, JsonNode? schema) in old[map]?.AsObject() ?? [])
            {
                Compare(schema, now[map]?[name], $"{path}/{map}/{name}", breaks);
            }
        }

        foreach (string list in Lists)
        {
            JsonArray oldList = old[list]?.AsArray() ?? [];
            JsonArray? newList = now[list]?.AsArray();
            for (int index = 0; index < oldList.Count; index++)
            {
                Compare(oldList[index], newList is not null && index < newList.Count ? newList[index] : null, $"{path}/{list}/{index}", breaks);
            }
        }

        foreach (string node in Nodes)
        {
            if (old[node] is JsonNode schema)
            {
                Compare(schema, now[node], $"{path}/{node}", breaks);
            }
        }
    }

    private static string[] Strings(JsonNode? node) => node switch
    {
        JsonArray array => [.. array.Select(item => item!.ToJsonString())],
        null => [],
        _ => [node.ToJsonString()],
    };
}
