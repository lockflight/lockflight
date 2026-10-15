using System.Globalization;
using System.Text.Json;

namespace Lockflight.Cli.Review;

/// <summary>
/// Reads <c>claude -p --output-format json --json-schema …</c> output. The schema-validated object arrives as the <c>result</c>
/// string, so it is parsed a second time.
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
        return new ReviewOutcome(Text(body, "verdict"), Text(body, "summary"), findings, cost, duration, null);
    }

    private static ReviewFinding ReadFinding(JsonElement finding) => new(
        ReviewNames.ParseSeverity(Text(finding, "severity")),
        Text(finding, "file"),
        finding.TryGetProperty("line", out JsonElement line) && line.TryGetInt32(out int number) ? number : 0,
        Text(finding, "requirement") is { Length: > 0 } requirement ? requirement : null,
        Text(finding, "title"),
        Text(finding, "evidence"),
        Text(finding, "reproduction") is { Length: > 0 } reproduction && reproduction.Contains("[Fact", StringComparison.Ordinal) ? reproduction : null,
        FindingStatus.Reported);

    private static ReviewOutcome Failed(string error) => new(string.Empty, string.Empty, [], 0m, 0, error);

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string First(string text) => text.Length > 200 ? text[..200].ToString(CultureInfo.InvariantCulture) : text;
}
