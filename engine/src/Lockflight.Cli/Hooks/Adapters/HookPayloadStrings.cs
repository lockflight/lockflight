using System.Text.Json;

namespace Lockflight.Cli.Hooks.Adapters;

/// <summary>Reading strings out of a hook payload, the same way for every adapter.</summary>
internal static class HookPayloadStrings
{
    public static string? Read(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Every string value (and property name) inside <paramref name="element"/>, decoded, at any depth.</summary>
    public static void Collect(JsonElement element, List<string> strings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                strings.Add(element.GetString()!);
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    strings.Add(property.Name);
                    Collect(property.Value, strings);
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Collect(item, strings);
                }

                break;
        }
    }
}
