using System.Text.Json;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// Finds a property name that appears twice in one JSON object. System.Text.Json keeps both and its lookups take the last, while
/// the shell readers of the same file (<c>install.sh</c>, the hook shim) take the first: a file the two would read differently is
/// refused rather than read either way.
/// </summary>
internal static class JsonDuplicateKeys
{
    /// <summary>The dotted path of the first duplicated property, such as <c>binaries.linux-x64</c>; null when there is none.</summary>
    public static string? Find(JsonElement element) => Find(element, string.Empty);

    private static string? Find(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(item => Find(item, path)).FirstOrDefault(found => found is not null);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string propertyPath = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
            if (!seen.Add(property.Name))
            {
                return propertyPath;
            }

            if (Find(property.Value, propertyPath) is string nested)
            {
                return nested;
            }
        }

        return null;
    }
}
