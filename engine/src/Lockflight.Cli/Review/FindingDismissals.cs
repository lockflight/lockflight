using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Review;

/// <summary>Dismissals in <c>.lockflight/features/&lt;id&gt;/reviews/dismissals/&lt;finding&gt;.json</c>, one per finding (protected; engine-written).</summary>
internal static class FindingDismissals
{
    private const string Schema = "lockflight.dismissal/v1";
    private const string DirectoryName = "dismissals";

    public static string Save(string recordDirectory, FindingDismissal dismissal)
    {
        JsonObject record = new()
        {
            ["schema"] = Schema,
            ["finding"] = dismissal.FindingId,
            ["severity"] = ReviewNames.ToName(dismissal.Severity),
            ["file"] = dismissal.File,
            ["line"] = dismissal.Line,
            ["title"] = dismissal.Title,
            ["by"] = dismissal.By,
            ["at"] = dismissal.At.ToString("O", CultureInfo.InvariantCulture),
            ["reason"] = dismissal.Reason,
        };
        string path = Path.Combine(ReviewEvidence.DirectoryOf(recordDirectory), DirectoryName, $"{dismissal.FindingId}.json");
        JsonFiles.Write(path, record);
        return path;
    }

    /// <summary>Every readable dismissal, oldest first.</summary>
    public static IReadOnlyList<FindingDismissal> Read(string recordDirectory)
    {
        string directory = Path.Combine(ReviewEvidence.DirectoryOf(recordDirectory), DirectoryName);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<FindingDismissal> dismissals = [];
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                JsonElement root = document.RootElement;
                dismissals.Add(new FindingDismissal(
                    Text(root, "finding"),
                    ReviewNames.ParseSeverity(Text(root, "severity")),
                    Text(root, "file"),
                    root.TryGetProperty("line", out JsonElement line) && line.TryGetInt32(out int number) ? number : 0,
                    Text(root, "title"),
                    Text(root, "by"),
                    DateTimeOffset.Parse(Text(root, "at"), CultureInfo.InvariantCulture),
                    Text(root, "reason")));
            }
            catch (Exception exception) when (exception is JsonException or FormatException)
            {
                // Not a dismissal this engine wrote: it closes nothing.
            }
        }

        return [.. dismissals.Where(dismissal => dismissal.FindingId.Length > 0).OrderBy(dismissal => dismissal.At)];
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
