using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Review;

/// <summary>
/// Stores every review run in <c>.lockflight/features/&lt;id&gt;/reviews/&lt;run&gt;.json</c> (protected; engine-written) and reads
/// them back, oldest first, into the feature's review history: runs, and the open and fixed findings after the last run and the
/// dismissals (<see cref="FindingDismissals"/>).
/// </summary>
internal static class ReviewEvidence
{
    public const string DirectoryName = "reviews";

    private const string Schema = "lockflight.review/v2";
    private const string FirstSchema = "lockflight.review/v1";
    private const string BeforeTheRules = "recorded before review-of-record rules existed";

    public static string DirectoryOf(string recordDirectory) => Path.Combine(recordDirectory, DirectoryName);

    /// <summary>A run id from the time, unique in the feature's review directory.</summary>
    public static string NewRunId(string recordDirectory, DateTimeOffset at)
    {
        string stem = at.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string id = stem;
        for (int suffix = 2; File.Exists(Path.Combine(DirectoryOf(recordDirectory), $"{id}.json")); suffix++)
        {
            id = $"{stem}-{suffix}";
        }

        return id;
    }

    public static string Save(string recordDirectory, ReviewRun run, ReviewOutcome outcome)
    {
        JsonArray findings = [];
        foreach (ReviewFinding finding in outcome.Findings)
        {
            findings.Add((JsonNode)new JsonObject
            {
                ["id"] = finding.Id,
                ["dimension"] = finding.Dimension,
                ["severity"] = ReviewNames.ToName(finding.Severity),
                ["outcome"] = ReviewNames.ToName(finding.Outcome),
                ["reproductionResult"] = ReviewNames.ToName(finding.Reproduced),
                ["state"] = finding.State is FindingState state ? ReviewNames.ToName(state) : null,
                ["carried"] = finding.Carried,
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
            ["run"] = run.Id,
            ["at"] = run.At.ToString("O", CultureInfo.InvariantCulture),
            ["feature"] = run.Feature,
            ["base"] = new JsonObject
            {
                ["ref"] = run.Base.Ref,
                ["commit"] = run.Base.Commit,
                ["mergeBase"] = run.Base.MergeBase,
                ["isMergeBase"] = run.Base.IsMergeBase,
            },
            ["head"] = run.Head,
            ["diffSha256"] = run.DiffSha256,
            ["promptSha256"] = run.PromptSha256,
            ["model"] = run.Model,
            ["budgetUsd"] = run.BudgetUsd,
            ["settings"] = new JsonObject
            {
                ["model"] = run.Settings.Model,
                ["maxUsd"] = run.Settings.BudgetUsd,
                ["source"] = run.Settings.FromManifest ? "manifest" : "default",
            },
            ["claudeCodeVersion"] = run.ClaudeCodeVersion,
            ["usage"] = new JsonObject
            {
                ["inputTokens"] = outcome.Usage.InputTokens,
                ["outputTokens"] = outcome.Usage.OutputTokens,
                ["cacheReadInputTokens"] = outcome.Usage.CacheReadInputTokens,
                ["cacheCreationInputTokens"] = outcome.Usage.CacheCreationInputTokens,
            },
            ["estimatedCostUsd"] = outcome.CostUsd,
            ["claudeDurationMs"] = outcome.DurationMs,
            ["wallTimeMs"] = run.WallTimeMs,
            ["ofRecord"] = run.OfRecord,
            ["notOfRecord"] = new JsonArray([.. run.NotOfRecord.Select(reason => (JsonNode)JsonValue.Create(reason))]),
            ["verdict"] = outcome.Verdict,
            ["summary"] = outcome.Summary,
            ["findings"] = findings,
        };
        string path = Path.Combine(DirectoryOf(recordDirectory), $"{run.Id}.json");
        JsonFiles.Write(path, record);
        return path;
    }

    /// <summary>The feature's review history; unreadable run files are skipped.</summary>
    public static ReviewHistory Load(string recordDirectory)
    {
        string directory = DirectoryOf(recordDirectory);
        if (!Directory.Exists(directory))
        {
            return ReviewHistory.Empty;
        }

        List<ReviewRunSummary> runs = [];
        Dictionary<string, TrackedFinding> open = new(StringComparer.Ordinal);
        Dictionary<string, TrackedFinding> fixedFindings = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                if (ReadRun(document.RootElement, Path.GetFileNameWithoutExtension(file), open, fixedFindings) is ReviewRunSummary run)
                {
                    runs.Add(run);
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
            {
                // Not a run this engine wrote: it neither opens nor closes anything.
            }
        }

        IReadOnlyList<FindingDismissal> dismissals = FindingDismissals.Read(recordDirectory);
        foreach (FindingDismissal dismissal in dismissals)
        {
            open.Remove(dismissal.FindingId);
        }

        return new ReviewHistory(runs, [.. open.Values.OrderBy(tracked => tracked.Finding.Severity)], [.. fixedFindings.Values], dismissals);
    }

    private static ReviewRunSummary? ReadRun(
        JsonElement root, string runId, Dictionary<string, TrackedFinding> open, Dictionary<string, TrackedFinding> fixedFindings)
    {
        string schema = Text(root, "schema");
        if (schema is not (Schema or FirstSchema))
        {
            return null;
        }

        DateTimeOffset at = DateTimeOffset.Parse(Text(root, "at"), CultureInfo.InvariantCulture);
        decimal cost = Number(root, schema == Schema ? "estimatedCostUsd" : "costUsd");
        if (schema == FirstSchema)
        {
            return new ReviewRunSummary(runId, at, Text(root, "model"), Text(root, "base"), null, [BeforeTheRules], cost);
        }

        foreach (JsonElement element in root.GetProperty("findings").EnumerateArray())
        {
            ReviewFinding finding = ReadFinding(element);
            switch (finding.State)
            {
                case FindingState.Open:
                    string firstRun = open.TryGetValue(finding.Id, out TrackedFinding? earlier) ? earlier.FirstRun : runId;
                    open[finding.Id] = new TrackedFinding(finding, firstRun, runId);
                    fixedFindings.Remove(finding.Id);
                    break;
                case FindingState.Fixed:
                    fixedFindings[finding.Id] = new TrackedFinding(finding, open.Remove(finding.Id, out TrackedFinding? opened) ? opened.FirstRun : runId, runId);
                    break;
                case FindingState.Dismissed:
                    open.Remove(finding.Id);
                    break;
            }
        }

        JsonElement reviewBase = root.GetProperty("base");
        string[] notOfRecord = [.. root.GetProperty("notOfRecord").EnumerateArray().Select(reason => reason.GetString() ?? string.Empty)];
        string? mergeBase = reviewBase.TryGetProperty("mergeBase", out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return new ReviewRunSummary(runId, at, Text(root, "model"), Text(reviewBase, "ref"), mergeBase, notOfRecord, cost);
    }

    private static ReviewFinding ReadFinding(JsonElement element) =>
        new(
            ReviewNames.ParseSeverity(Text(element, "severity")),
            Text(element, "file"),
            element.TryGetProperty("line", out JsonElement line) && line.TryGetInt32(out int number) ? number : 0,
            Text(element, "requirement") is { Length: > 0 } requirement ? requirement : null,
            Text(element, "title"),
            Text(element, "evidence"),
            Text(element, "reproduction") is { Length: > 0 } reproduction ? reproduction : null,
            ReviewNames.ParseOutcome(Text(element, "outcome")))
        {
            Id = Text(element, "id"),
            Dimension = Text(element, "dimension"),
            State = ReviewNames.ParseState(Text(element, "state")),
        };

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static decimal Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetDecimal(out decimal number) ? number : 0m;
}
