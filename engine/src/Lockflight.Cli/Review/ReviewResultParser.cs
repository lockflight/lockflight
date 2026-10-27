using System.Globalization;
using System.Text.Json;

namespace Lockflight.Cli.Review;

/// <summary>
/// Reads <c>claude -p --output-format json --json-schema …</c> output. The schema-validated object arrives in
/// <c>structured_output</c>; older Claude Code versions put it in the <c>result</c> string instead, which is then parsed a second
/// time. A run that ends without valid structured output has neither, and fails as not JSON.
/// </summary>
internal static class ReviewResultParser
{
    public static ReviewOutcome Parse(string output)
    {
        int start = output.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return Failed($"no JSON from claude: {First(output)}");
        }

        using JsonDocument envelope = JsonDocument.Parse(output[start..]);
        JsonElement root = envelope.RootElement;
        decimal cost = root.TryGetProperty("total_cost_usd", out JsonElement costElement) && costElement.TryGetDecimal(out decimal value) ? value : 0m;
        long duration = root.TryGetProperty("duration_ms", out JsonElement durationElement) && durationElement.TryGetInt64(out long ms) ? ms : 0;
        if (root.TryGetProperty("is_error", out JsonElement isError) && isError.ValueKind == JsonValueKind.True)
        {
            return Failed($"claude reported an error: {Text(root, "result")}") with { CostUsd = cost, DurationMs = duration };
        }

        string result = root.TryGetProperty("structured_output", out JsonElement structured) && structured.ValueKind == JsonValueKind.Object
            ? structured.GetRawText()
            : Text(root, "result");
        int objectStart = result.IndexOf('{', StringComparison.Ordinal);
        if (objectStart < 0)
        {
            return Failed($"the review is not JSON: {First(result)}") with { CostUsd = cost, DurationMs = duration };
        }

        using JsonDocument review = JsonDocument.Parse(result[objectStart..(result.LastIndexOf('}') + 1)]);
        JsonElement body = review.RootElement;
        ReviewFinding[] findings = body.TryGetProperty("findings", out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Select(ReadFinding)]
            : [];
        return new ReviewOutcome(Text(body, "verdict"), Text(body, "summary"), findings, cost, duration, null) { Usage = ReadUsage(root) };
    }

    /// <summary>Tokens summed over <c>modelUsage</c> (every model the call used), else read from <c>usage</c>.</summary>
    private static ReviewUsage ReadUsage(JsonElement root)
    {
        if (root.TryGetProperty("modelUsage", out JsonElement models) && models.ValueKind == JsonValueKind.Object)
        {
            JsonElement[] perModel = [.. models.EnumerateObject().Select(model => model.Value).Where(model => model.ValueKind == JsonValueKind.Object)];
            if (perModel.Length > 0)
            {
                return new ReviewUsage(
                    perModel.Sum(model => Count(model, "inputTokens")),
                    perModel.Sum(model => Count(model, "outputTokens")),
                    perModel.Sum(model => Count(model, "cacheReadInputTokens")),
                    perModel.Sum(model => Count(model, "cacheCreationInputTokens")));
            }
        }

        return root.TryGetProperty("usage", out JsonElement usage) && usage.ValueKind == JsonValueKind.Object
            ? new ReviewUsage(
                Count(usage, "input_tokens"), Count(usage, "output_tokens"), Count(usage, "cache_read_input_tokens"), Count(usage, "cache_creation_input_tokens"))
            : ReviewUsage.Empty;
    }

    private static long Count(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long count) ? count : 0;

    private static ReviewFinding ReadFinding(JsonElement finding)
    {
        string file = Text(finding, "file");
        int line = finding.TryGetProperty("line", out JsonElement lineElement) && lineElement.TryGetInt32(out int number) ? number : 0;
        string title = Text(finding, "title");
        return new ReviewFinding(
            ReviewNames.ParseSeverity(Text(finding, "severity")),
            file,
            line,
            Text(finding, "requirement") is { Length: > 0 } requirement ? requirement : null,
            title,
            Text(finding, "evidence"),
            Text(finding, "reproduction") is { Length: > 0 } reproduction && reproduction.Contains("[Fact", StringComparison.Ordinal) ? reproduction : null,
            FindingOutcome.Reported)
        {
            Id = FindingIds.Of(ReviewNames.GeneralDimension, file, line, title),
        };
    }

    private static ReviewOutcome Failed(string error) => new(string.Empty, string.Empty, [], 0m, 0, error);

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string First(string text) => text.Length > 200 ? text[..200].ToString(CultureInfo.InvariantCulture) : text;
}
