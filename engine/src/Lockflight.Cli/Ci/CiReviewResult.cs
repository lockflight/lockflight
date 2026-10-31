using System.Text.Json.Nodes;

namespace Lockflight.Cli.Ci;

/// <summary>
/// <c>lockflight.ci-review/v1</c> (<c>schemas/lockflight.ci-review.v1.json</c>): the review job's result, which the report job joins
/// as data. The review job runs the reviewer's reproductions of the pull request's code, so the report checks its shape before it
/// uses the verdict: the schema id, a full head commit id, a verdict of PASS, FAIL, NOT-EARNED or ERROR, and a line of text.
/// </summary>
internal static class CiReviewResult
{
    public const string Schema = "lockflight.ci-review/v1";

    private static readonly string[] Verdicts = ["PASS", "FAIL", "NOT-EARNED", "ERROR"];

    /// <summary>What makes <paramref name="result"/> not a <c>lockflight.ci-review/v1</c> document; null when it is one.</summary>
    public static string? Problem(JsonObject result)
    {
        if (Text(result["schema"]) != Schema)
        {
            return $"'schema' must be {Schema}";
        }

        if (Text(result["head"]) is not string head || !CommitIds.IsFull(head))
        {
            return "'head' must be a full commit id";
        }

        if (Text(result["verdict"]) is not string verdict || !Verdicts.Contains(verdict, StringComparer.Ordinal))
        {
            return $"'verdict' must be one of {string.Join(", ", Verdicts)}";
        }

        return result.ContainsKey("why") && Text(result["why"]) is null ? "'why' must be a string" : null;
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
