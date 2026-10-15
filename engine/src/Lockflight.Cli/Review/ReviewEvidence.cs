using System.Globalization;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Review;

/// <summary>Stores the review of record in <c>.lockflight/features/&lt;id&gt;/reviews/&lt;timestamp&gt;.json</c> (protected; engine-written).</summary>
internal static class ReviewEvidence
{
    private const string Schema = "lockflight.review/v1";

    public static string Save(string recordDirectory, ReviewOutcome outcome, string baseRef, string head, string diffHash, string model, DateTimeOffset at)
    {
        JsonArray findings = [];
        foreach (ReviewFinding finding in outcome.Findings)
        {
            findings.Add((JsonNode)new JsonObject
            {
                ["severity"] = ReviewNames.ToName(finding.Severity),
                ["status"] = ReviewNames.ToName(finding.Status),
                ["file"] = finding.File,
                ["line"] = finding.Line,
                ["requirement"] = finding.Requirement,
                ["title"] = finding.Title,
                ["evidence"] = finding.Evidence,
                ["reproduction"] = finding.Reproduction,
            });
        }

        JsonObject record = new()
        {
            ["schema"] = Schema,
            ["at"] = at.ToString("O", CultureInfo.InvariantCulture),
            ["base"] = baseRef,
            ["head"] = head,
            ["diffSha256"] = diffHash,
            ["model"] = model,
            ["costUsd"] = outcome.CostUsd,
            ["durationMs"] = outcome.DurationMs,
            ["verdict"] = outcome.Verdict,
            ["summary"] = outcome.Summary,
            ["findings"] = findings,
        };
        string path = Path.Combine(recordDirectory, "reviews", $"{at:yyyyMMdd-HHmmss}.json");
        JsonFiles.Write(path, record);
        return path;
    }
}
