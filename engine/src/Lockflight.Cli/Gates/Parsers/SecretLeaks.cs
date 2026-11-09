using System.Text.Json;

namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// Reads a gitleaks JSON report: an array of findings with <c>RuleID</c>, <c>File</c>, <c>StartLine</c> and <c>Commit</c>. The
/// <c>Match</c> and <c>Secret</c> are read only to leave out the engine's own recorded hashes (<see cref="EngineEvidenceHashes"/>)
/// and are not kept.
/// </summary>
internal static class SecretLeaks
{
    /// <summary>How many findings the report lists before any is left out; 0 for an empty report.</summary>
    /// <exception cref="JsonException">The report is not JSON.</exception>
    public static int Reported(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : 0;
    }

    public static IReadOnlyList<SecretLeak> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. document.RootElement.EnumerateArray()
            .Where(finding => !EngineEvidenceHashes.Recognize(Text(finding, "RuleID"), Text(finding, "File"), Text(finding, "Match"), Text(finding, "Secret")))
            .Select(finding => new SecretLeak(
                Text(finding, "RuleID"),
                Text(finding, "File").Replace('\\', '/'),
                finding.TryGetProperty("StartLine", out JsonElement line) && line.TryGetInt32(out int number) ? number : 0,
                Text(finding, "Commit")))
            .Distinct()];
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
