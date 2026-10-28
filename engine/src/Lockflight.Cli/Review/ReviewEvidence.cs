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
    private const string WithoutRecordStatus = "the run file does not say whether it is the review of record";
    private const string WithoutBase = "the run file records no base";

    /// <summary>The length of the time part of a run id after its last dash (<c>yyyyMMdd-HHmmss</c>).</summary>
    private const int TimeLength = 6;

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
                ["specQuote"] = finding.SpecQuote,
                ["specAnchor"] = ReviewNames.ToName(finding.SpecAnchor),
                ["title"] = finding.Title,
                ["evidence"] = finding.Evidence,
                ["reproduction"] = finding.Reproduction,
                ["earlierReproductions"] = new JsonArray([.. finding.EarlierReproductions.Select(earlier => (JsonNode)JsonValue.Create(earlier))]),
                ["reproductionAttempts"] = finding.ReproductionAttempts,
                ["reproductionProject"] = finding.ReproductionProject,
                ["compileErrors"] = new JsonArray([.. finding.CompileErrors.Select(errors => (JsonNode)JsonValue.Create(errors))]),
                ["fix"] = finding.Fix,
                ["fixCheck"] = ReviewNames.ToName(finding.FixCheck),
                ["fixChecked"] = finding.FixChecked,
                ["fixAttempts"] = finding.FixAttempts,
                ["ownTestsChecked"] = finding.OwnTestsChecked,
                ["conflictingTests"] = new JsonArray([.. finding.ConflictingTests.Select(test => (JsonNode)JsonValue.Create(test))]),
                ["fixCheckErrors"] = finding.FixCheckErrors,
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
                ["target"] = run.Base.Target,
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
                ["target"] = run.Settings.Target,
                ["maxDiffCharacters"] = run.Settings.MaxDiffCharacters,
                ["source"] = run.Settings.FromManifest ? "manifest" : "default",
            },
            ["generatedFiles"] = new JsonObject
            {
                ["status"] = GeneratedFiles.Status,
                ["files"] = new JsonArray([.. run.Generated.Select(file => (JsonNode)new JsonObject
                {
                    ["path"] = file.Path,
                    ["bytes"] = file.Bytes,
                    ["sha256"] = file.Sha256,
                })]),
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
            ["reproductionRepairs"] = new JsonObject
            {
                ["calls"] = outcome.Repairs.Calls,
                ["estimatedCostUsd"] = outcome.Repairs.CostUsd,
            },
            ["fixChecks"] = new JsonObject
            {
                ["count"] = outcome.FixChecks.Count,
                ["wallTimeMs"] = outcome.FixChecks.WallTimeMs,
            },
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

    /// <summary>
    /// The feature's review history. Runs are replayed in the order they were recorded (their time, then the number a run id gets
    /// when several start in the same second), not by file name. A run file this engine cannot read (not JSON, no time, no
    /// findings) is skipped and named in <see cref="ReviewHistory.Unreadable"/>; a file of another schema is not a run.
    /// </summary>
    public static ReviewHistory Load(string recordDirectory)
    {
        string directory = DirectoryOf(recordDirectory);
        if (!Directory.Exists(directory))
        {
            return ReviewHistory.Empty;
        }

        List<string> unreadable = [];
        List<StoredRun> stored = [];
        foreach (string file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                if (Stored(document.RootElement.Clone(), Path.GetFileNameWithoutExtension(file), out string? problem) is StoredRun run)
                {
                    stored.Add(run);
                }
                else if (problem is not null)
                {
                    unreadable.Add($"{DirectoryName}/{name}: {problem}; the run is ignored");
                }
            }
            catch (JsonException exception)
            {
                unreadable.Add($"{DirectoryName}/{name}: not valid JSON ({exception.Message}); the run is ignored");
            }
        }

        List<ReviewRunSummary> runs = [];
        Dictionary<string, TrackedFinding> open = new(StringComparer.Ordinal);
        Dictionary<string, TrackedFinding> fixedFindings = new(StringComparer.Ordinal);
        foreach (StoredRun run in stored.OrderBy(run => run.At).ThenBy(run => run.Sequence).ThenBy(run => run.Id, StringComparer.Ordinal))
        {
            runs.Add(ReadRun(run, open, fixedFindings));
        }

        IReadOnlyList<FindingDismissal> dismissals = FindingDismissals.Read(recordDirectory);
        foreach (FindingDismissal dismissal in dismissals)
        {
            open.Remove(dismissal.FindingId);
        }

        return new ReviewHistory(runs, [.. open.Values.OrderBy(tracked => tracked.Finding.Severity)], [.. fixedFindings.Values], dismissals) { Unreadable = unreadable };
    }

    /// <summary>The run a file holds; null with the reason when it is unreadable, or with none when it is not a run file at all.</summary>
    private static StoredRun? Stored(JsonElement root, string id, out string? problem)
    {
        problem = null;
        string schema = root.ValueKind == JsonValueKind.Object ? Text(root, "schema") : string.Empty;
        if (schema is not (Schema or FirstSchema))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(Text(root, "at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset at))
        {
            problem = "missing or unreadable 'at'";
            return null;
        }

        if (schema == Schema && !(root.TryGetProperty("findings", out JsonElement findings) && findings.ValueKind == JsonValueKind.Array))
        {
            problem = "missing 'findings'";
            return null;
        }

        return new StoredRun(id, at, Sequence(id), schema, root);
    }

    /// <summary>A run id is the time, with <c>-2</c>, <c>-3</c>… when that id was taken (<see cref="NewRunId"/>).</summary>
    private static int Sequence(string id)
    {
        int dash = id.LastIndexOf('-');
        return dash > 0 && id.Length - dash - 1 != TimeLength && int.TryParse(id.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int sequence) ? sequence : 1;
    }

    private static ReviewRunSummary ReadRun(StoredRun run, Dictionary<string, TrackedFinding> open, Dictionary<string, TrackedFinding> fixedFindings)
    {
        JsonElement root = run.Root;
        decimal cost = Number(root, run.Schema == Schema ? "estimatedCostUsd" : "costUsd");
        if (run.Schema == FirstSchema)
        {
            return new ReviewRunSummary(run.Id, run.At, Text(root, "model"), new ReviewBase(Text(root, "base"), null, string.Empty, null), [BeforeTheRules], cost);
        }

        foreach (ReviewFinding finding in root.GetProperty("findings").EnumerateArray().Where(element => element.ValueKind == JsonValueKind.Object).Select(ReadFinding))
        {
            switch (finding.State)
            {
                case FindingState.Open:
                    string firstRun = open.TryGetValue(finding.Id, out TrackedFinding? earlier) ? earlier.FirstRun : run.Id;
                    open[finding.Id] = new TrackedFinding(finding, firstRun, run.Id);
                    fixedFindings.Remove(finding.Id);
                    break;
                case FindingState.Fixed:
                    fixedFindings[finding.Id] = new TrackedFinding(finding, open.Remove(finding.Id, out TrackedFinding? opened) ? opened.FirstRun : run.Id, run.Id);
                    break;
                case FindingState.Dismissed:
                    open.Remove(finding.Id);
                    break;
            }
        }

        bool hasBase = root.TryGetProperty("base", out JsonElement reviewBase) && reviewBase.ValueKind == JsonValueKind.Object;
        ReviewBase recordedBase = hasBase
            ? new(Text(reviewBase, "ref"), OptionalText(reviewBase, "commit"), Text(reviewBase, "target"), OptionalText(reviewBase, "mergeBase"))
            : new(string.Empty, null, string.Empty, null);
        List<string> notOfRecord = root.TryGetProperty("notOfRecord", out JsonElement reasons) && reasons.ValueKind == JsonValueKind.Array
            ? [.. reasons.EnumerateArray().Select(reason => reason.ValueKind == JsonValueKind.String ? reason.GetString() ?? string.Empty : reason.ToString())]
            : [WithoutRecordStatus];
        if (!hasBase)
        {
            notOfRecord.Add(WithoutBase);
        }

        return new ReviewRunSummary(run.Id, run.At, Text(root, "model"), recordedBase, notOfRecord, cost);
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
            EarlierReproductions = element.TryGetProperty("earlierReproductions", out JsonElement earlier) && earlier.ValueKind == JsonValueKind.Array
                ? [.. earlier.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
                : [],
            State = ReviewNames.ParseState(Text(element, "state")),
            SpecQuote = OptionalText(element, "specQuote"),
            SpecAnchor = ReviewNames.ParseSpecAnchor(Text(element, "specAnchor")),
            Fix = OptionalText(element, "fix"),
            FixCheck = ReviewNames.ParseFixCheck(Text(element, "fixCheck")),
            ConflictingTests = element.TryGetProperty("conflictingTests", out JsonElement conflicting) && conflicting.ValueKind == JsonValueKind.Array
                ? [.. conflicting.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
                : [],
        };

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string? OptionalText(JsonElement element, string property) => Text(element, property) is { Length: > 0 } text ? text : null;

    private static decimal Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.TryGetDecimal(out decimal number) ? number : 0m;
}
