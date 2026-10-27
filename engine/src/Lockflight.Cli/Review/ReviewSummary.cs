namespace Lockflight.Cli.Review;

/// <summary>
/// The review part of a pull request body: the base of the latest run, how many runs there were and whether the latest is of record,
/// and every open blocker/major finding. The run count and the open findings make a re-rolled or skipped review visible.
/// </summary>
internal static class ReviewSummary
{
    public static IReadOnlyList<string> PullRequest(ReviewHistory history)
    {
        if (history.Runs.Count == 0)
        {
            return ["**Lockflight review:** no review runs yet."];
        }

        ReviewRunSummary latest = history.Runs[^1];
        string mergeBase = latest.MergeBase is null ? "no merge base" : $"merge base `{ReviewOfRecord.Short(latest.MergeBase)}`";
        string record = latest.OfRecord ? "is the review of record" : $"is not the review of record ({string.Join("; ", latest.NotOfRecord)})";
        List<string> lines =
        [
            $"**Lockflight review:** base `{latest.BaseRef}` ({mergeBase}), {Runs(history.Runs.Count)}; the latest ({latest.Model}) {record}.",
            $"Open blocker/major findings: {history.Open.Count} · fixed: {history.Fixed.Count} · dismissed by a person: {history.Dismissed.Count}",
        ];
        lines.AddRange(history.Open.Select(tracked => $"- {Line(tracked.Finding)}"));
        return lines;
    }

    /// <summary>"1 review run", "3 review runs".</summary>
    public static string Runs(int count) => count == 1 ? "1 review run" : $"{count} review runs";

    public static string Line(ReviewFinding finding) =>
        $"`{finding.Id}` {ReviewNames.ToName(finding.Severity)}, {ReviewNames.ToName(finding.Outcome)}: `{finding.File}:{finding.Line}` {finding.Title}";
}
