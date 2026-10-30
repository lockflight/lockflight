using System.Text;
using System.Text.Json;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// A feature's recorded class, <c>.lockflight/features/&lt;id&gt;/class.json</c> (<c>{ "class": "feature" }</c>): the highest class a
/// pull request of the feature was judged at, committed with the feature's records. On the base branch it raises the feature's
/// later pull requests, so a change split into small pull requests keeps the class of its first. It is a committed file, so a
/// pull request can edit it, but only to undo a raise it added itself: it never lowers the computed class.
/// </summary>
internal static class FeatureClassRecord
{
    public const string FileName = "class.json";
    private const string ClassProperty = "class";

    public static string PathFor(string featureId) => $".lockflight/features/{featureId}/{FileName}";

    /// <summary>The recorded class in a record's text; null when the text is not a record or names no class.</summary>
    public static ChangeClass? Parse(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(ClassProperty, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && ChangeClassNames.TryParse(value.GetString(), out ChangeClass changeClass)
                ? changeClass
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Render(ChangeClass changeClass)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString(ClassProperty, ChangeClassNames.ToName(changeClass));
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }
}
