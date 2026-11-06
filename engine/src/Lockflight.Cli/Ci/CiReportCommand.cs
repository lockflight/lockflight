using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Evidence;
using Lockflight.Cli.Evidence.Blocked;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Ci;

/// <summary>
/// <c>lockflight ci report</c>: the report job's step. It reads the judge's evidence and the review job's result as data (never
/// running anything from them), joins the review into its check, recomputes both status contexts, and writes the evidence of record
/// (<c>verdict.json</c>), the PR comment (<c>comment.md</c>, within GitHub's limit) and the job summary (<c>summary.md</c>). The exit
/// code is the verdict of <c>--context</c> (<c>gates</c> or <c>lockflight/judge</c>), else of the whole run. A missing judge
/// artifact is NOT-EARNED ("the judge did not finish"); one the report refuses to read (<see cref="CiArtifactReader"/>: too large, too
/// deep, a property named twice, not a JSON object) is ERROR, as is evidence about another head; a review about another head is ignored.
/// With <c>--base</c> (the target branch's tip, checked out, and the head fetched) the report re-plans the run from git and refuses
/// evidence whose policy, engine, event, class, red commits or check levels disagree with it (<see cref="EvidenceCrossCheck"/>).
/// </summary>
internal sealed class CiReportCommand(CliContext context) : ICommand
{
    public const string EvidenceOption = "--evidence";
    public const string ReviewOption = "--review";
    public const string HeadOption = "--head";
    public const string ContextOption = "--context";
    public const string OutOption = "--out";
    public const string BaseOption = "--base";
    public const string BranchOption = "--branch";
    public const string EventOption = "--event";
    public const string PlatformOption = "--platform";

    public const string VerdictFileName = "verdict.json";
    public const string CommentFileName = "comment.md";
    public const string SummaryFileName = "summary.md";

    /// <summary>The review job's result file the report joins: <c>lockflight.ci-review/v1</c> (<see cref="CiReviewResult"/>).</summary>
    public const string ReviewFileName = "review-result.json";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        if (options.Value(EvidenceOption) is not string evidenceDirectory || options.Value(HeadOption) is not string head || !CommitIds.IsFull(head)
            || options.Value(OutOption) is not string outDirectory)
        {
            await context.Error.WriteLineAsync($"lockflight ci report: {EvidenceOption} <dir>, {HeadOption} <full sha> and {OutOption} <dir> are required");
            return ExitCode.Config;
        }

        StatusContext? only = null;
        if (options.Value(ContextOption) is string contextName)
        {
            if (StatusContextNames.Parse(contextName) is not StatusContext parsed)
            {
                await context.Error.WriteLineAsync($"lockflight ci report: {ContextOption} must be gates or lockflight/judge, not '{TerminalText.Clean(contextName)}'");
                return ExitCode.Config;
            }

            only = parsed;
        }

        string output = Path.GetFullPath(outDirectory, context.WorkingDirectory);
        CiArtifactReading evidenceReading = CiArtifactReader.Read(Path.GetFullPath(Path.Combine(evidenceDirectory, CiEvidence.FileName), context.WorkingDirectory));
        CiArtifactReading reviewReading = options.Value(ReviewOption) is string review
            ? CiArtifactReader.Read(Path.GetFullPath(Path.Combine(review, ReviewFileName), context.WorkingDirectory))
            : CiArtifactReading.None;
        JsonObject? evidence = evidenceReading.Document;
        JsonObject verdict = JoinArtifacts(evidenceReading, head, reviewReading);
        // Join returns the evidence itself only when it read it as the judge's; anything else is already an ERROR or NOT-EARNED.
        if (options.Value(BaseOption) is string baseCommit && ReferenceEquals(verdict, evidence))
        {
            if (!CommitIds.IsFull(baseCommit) || JudgeEventNames.Parse(options.Value(EventOption) ?? "pr") is not JudgeEvent judgeEvent
                || JudgePlatformNames.Parse(options.Value(PlatformOption) ?? JudgePlatformNames.ToName(JudgePlatform.GitHub)) is not JudgePlatform platform)
            {
                await context.Error.WriteLineAsync($"lockflight ci report: {BaseOption} must be a full commit id, {EventOption} pr, push or merge-group, {PlatformOption} {JudgePlatformNames.Accepted}");
                return ExitCode.Config;
            }

            // A push without a usable previous commit was judged without a base: the policy step then names the head itself.
            string? planBase = judgeEvent != JudgeEvent.PullRequest && baseCommit == head ? null : baseCommit;
            JudgeInputs inputs = new(context.WorkingDirectory, head, planBase, options.Value(BranchOption), platform, judgeEvent, output, null, ReviewExpected: true);
            // As the judge does in a CI clone: the pull request's own .gitattributes must not hide lines from routing.
            if (platform != JudgePlatform.Local && await JudgeAttributes.NeutraliseAsync(context.WorkingDirectory, cancellationToken) is string attributesError)
            {
                await context.Error.WriteLineAsync($"lockflight ci report: {attributesError}");
                return ExitCode.Error;
            }

            string? mismatch;
            try
            {
                mismatch = await EvidenceCrossCheck.MismatchAsync(evidence!, inputs, context.Environment, cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException or JsonException or KeyNotFoundException)
            {
                mismatch = $"it does not have the expected shape ({exception.Message})";
            }

            if (mismatch is not null)
            {
                verdict = Missing(head, $"the judge's evidence does not match what the report works out from git: {mismatch}", CheckVerdict.Error);
            }
        }

        JsonFiles.Write(Path.Combine(output, VerdictFileName), verdict);
        string comment;
        string summary;
        try
        {
            comment = CommentRenderer.Render(verdict, CommentRenderer.MaxCommentLength);
            summary = CommentRenderer.Summary(verdict);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            verdict = Missing(head, $"the judge's evidence could not be shown ({exception.Message})", CheckVerdict.Error);
            JsonFiles.Write(Path.Combine(output, VerdictFileName), verdict);
            comment = CommentRenderer.Render(verdict, CommentRenderer.MaxCommentLength);
            summary = CommentRenderer.Summary(verdict);
        }

        await File.WriteAllTextAsync(Path.Combine(output, CommentFileName), comment, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(output, SummaryFileName), summary, cancellationToken);

        string shown = only is StatusContext statusContext
            ? verdict["contexts"]?[StatusContextNames.ToName(statusContext)]?.GetValue<string>() ?? "ERROR"
            : verdict["verdict"]?.GetValue<string>() ?? "ERROR";
        await context.Output.WriteLineAsync($"{shown} {(only is StatusContext named ? StatusContextNames.ToName(named) : "lockflight ci")} for head {CommitIds.Short(head)}");
        if (CommentRenderer.Next(verdict) is string next)
        {
            await context.Output.WriteLineAsync($"Next: {TerminalText.Clean(next.ReplaceLineEndings(" "))}");
            if (CommentRenderer.NextCheck(verdict) is JsonNode check && CommentRenderer.ReasonOf(check) is ReasonCode reason && ReasonCatalogue.ConsoleLine(reason) is string line)
            {
                await context.Output.WriteLineAsync(line);
            }
        }

        return CheckVerdictNames.ToExitCode(CheckVerdictNames.Parse(shown) ?? CheckVerdict.Error);
    }

    /// <summary>
    /// The evidence with the review joined and the contexts and verdict recomputed from the checks; an artifact of the wrong shape
    /// is ERROR, never a crash that leaves no verdict.
    /// </summary>
    public static JsonObject Join(JsonObject? evidence, string head, JsonObject? review) =>
        JoinArtifacts(new CiArtifactReading(evidence, null), head, new CiArtifactReading(review, null));

    /// <summary>
    /// As <see cref="Join(JsonObject?, string, JsonObject?)"/> for the artifacts as read: evidence the report refused to read is
    /// ERROR, and a review result it refused makes the review check ERROR, each naming why.
    /// </summary>
    public static JsonObject JoinArtifacts(CiArtifactReading evidence, string head, CiArtifactReading review)
    {
        if (evidence.Problem is string evidenceProblem)
        {
            return Missing(head, CiEvidence.Cap($"the judge's evidence ({CiEvidence.FileName}) was not read: {evidenceProblem}"), CheckVerdict.Error);
        }

        try
        {
            return JoinChecked(evidence.Document, head, review);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or JsonException or KeyNotFoundException)
        {
            return Missing(head, $"the judge's evidence or the review's result does not have the expected shape ({exception.Message})", CheckVerdict.Error);
        }
    }

    private static JsonObject JoinChecked(JsonObject? evidence, string head, CiArtifactReading review)
    {
        if (evidence is null || evidence["schema"]?.GetValue<string>() != CiEvidence.Schema)
        {
            return Missing(head, "the judge did not finish: its evidence artifact is missing or not lockflight.ci-evidence/v1 (cancelled, killed at the platform's time limit, or failed to upload)");
        }

        if (evidence["head"]?.GetValue<string>() != head)
        {
            return Missing(head, $"the judge's evidence is about another head ({TerminalText.Clean(evidence["head"]?.GetValue<string>() ?? "none")}), not {head}: an artifact from another run", CheckVerdict.Error);
        }

        if (EvidenceShape.Problem(evidence) is string shape)
        {
            return Missing(head, $"the judge's evidence does not have the expected shape: {shape}", CheckVerdict.Error);
        }

        List<CheckResult> checks = [];
        foreach (JsonNode? node in evidence["checks"]?.AsArray() ?? [])
        {
            CheckResult? check = Parse(node);
            if (check is null)
            {
                return Missing(head, "the judge's evidence holds a check this engine cannot read", CheckVerdict.Error);
            }

            checks.Add(check.Id == CheckId.Review ? Review(check, review, head) : check);
        }

        if (Incomplete(checks, evidence["event"]?.GetValueKind() == JsonValueKind.String ? evidence["event"]!.GetValue<string>() : null) is string incomplete)
        {
            return Missing(head, $"the judge's evidence is not complete: {incomplete}", CheckVerdict.Error);
        }

        evidence["review"] = ReviewNode(review, head);
        evidence["checks"] = new JsonArray([.. checks.Select(check => (JsonNode)CiEvidence.CheckNode(check))]);
        evidence["contexts"] = new JsonObject
        {
            [StatusContextNames.ToName(StatusContext.Gates)] = CheckVerdictNames.ToName(JudgeVerdicts.Of(StatusContext.Gates, checks)),
            [StatusContextNames.ToName(StatusContext.Judge)] = CheckVerdictNames.ToName(JudgeVerdicts.Of(StatusContext.Judge, checks)),
        };
        CheckVerdict combined = JudgeVerdicts.Combine(checks);
        evidence["verdict"] = CheckVerdictNames.ToName(combined);
        if (combined != CheckVerdict.NotEarned)
        {
            // The review can turn the judge's NOT-EARNED into a FAIL: then no person is waited for.
            evidence["blocked"] = null;
        }
        else if (evidence["blocked"] is null && review.Document is null && review.Problem is null && EvidenceTime.Parse(evidence["finishedAt"]?.GetValue<string>()) is DateTimeOffset finishedAt
            && checks.Any(check => check is { Id: CheckId.Review, Level: CheckLevel.Required, Verdict: CheckVerdict.Skipped })
            && JudgeBlocked.OnlyWaitingOn(checks, check => check.Id == CheckId.Review))
        {
            evidence["blocked"] = BlockedReportJson.Render(JudgeBlocked.ForReview(finishedAt, head, evidence["feature"]?["id"]?.GetValue<string>()));
        }

        return evidence;
    }

    /// <summary>
    /// The review section of the verdict of record: <c>skipped</c> when no review ran, <c>unreadable</c> with why when the report
    /// refused its result, else <c>ran</c> or <c>stale</c> (about another head) with the review's head, verdict and line as data.
    /// </summary>
    private static JsonObject ReviewNode(CiArtifactReading reading, string head) => reading switch
    {
        { Problem: string problem } => new JsonObject { ["state"] = "unreadable", ["why"] = CiEvidence.Cap(problem) },
        { Document: JsonObject review } => new JsonObject
        {
            ["state"] = review["head"]?.GetValue<string>() == head ? "ran" : "stale",
            ["head"] = review["head"]?.GetValue<string>() is string reviewed ? CiEvidence.Cap(reviewed) : null,
            ["verdict"] = review["verdict"]?.GetValue<string>() is string verdict ? CiEvidence.Cap(verdict) : null,
            ["why"] = review["why"]?.GetValue<string>() is string why ? CiEvidence.Cap(why) : null,
        },
        _ => new JsonObject { ["state"] = "skipped" },
    };

    /// <summary>
    /// Why the checks cannot be what this engine's judge wrote: a check missing or listed twice, or green and the gates not
    /// required (they are on every event). An artifact that says less must not read as a pass.
    /// </summary>
    private static string? Incomplete(IReadOnlyList<CheckResult> checks, string? judgeEvent)
    {
        CheckId[] missing = [.. Enum.GetValues<CheckId>().Where(id => checks.Count(check => check.Id == id) != 1)];
        if (missing.Length > 0)
        {
            return $"checks missing or repeated: {string.Join(", ", missing.Select(CheckIdNames.ToName))}";
        }

        if (JudgeEventNames.Parse(judgeEvent ?? string.Empty) is null)
        {
            return "no known event";
        }

        return checks.Where(check => check.Id is CheckId.Green or CheckId.Gates).Any(check => check.Level != CheckLevel.Required)
            ? "green and the gates are not both required"
            : null;
    }

    /// <summary>The review job's verdict as the review check, or SKIPPED: no review ran, or it reviewed another head.</summary>
    private static CheckResult Review(CheckResult check, CiArtifactReading reading, string head)
    {
        if (reading.Problem is string unreadable)
        {
            return check.Judged(CheckVerdict.Error, ReasonCode.EvidenceInvalid, $"the review's result ({ReviewFileName}) was not read: {unreadable}");
        }

        if (reading.Document is not JsonObject review)
        {
            return check.Judged(CheckVerdict.Skipped, ReasonCode.ReviewNotRun, "no review ran (no API key configured, a fork, or the review job was skipped)");
        }

        if (CiReviewResult.Problem(review) is string problem)
        {
            return check.Judged(CheckVerdict.Error, ReasonCode.EvidenceInvalid, $"the review's result is not {CiReviewResult.Schema}: {problem}");
        }

        if (review["head"]?.GetValue<string>() != head)
        {
            return check.Judged(CheckVerdict.Skipped, ReasonCode.ReviewStale, "SKIPPED (stale): the review is about another head");
        }

        CheckVerdict verdict = CheckVerdictNames.Parse(review["verdict"]?.GetValue<string>() ?? string.Empty) ?? CheckVerdict.Error;
        return check.Judged(verdict, verdict is CheckVerdict.Pass or CheckVerdict.NotApplicable ? ReasonCode.None : ReasonCode.ReviewFindings, review["why"]?.GetValue<string>() ?? CheckVerdictNames.ToName(verdict));
    }

    private static CheckResult? Parse(JsonNode? node) =>
        CheckIdNames.Parse(node?["id"]?.GetValue<string>() ?? string.Empty) is CheckId id
        && CheckLevelNames.Parse(node?["requirement"]?.GetValue<string>() ?? string.Empty) is CheckLevel level
        && CheckVerdictNames.Parse(node?["verdict"]?.GetValue<string>() ?? string.Empty) is CheckVerdict verdict
            ? new CheckResult(id, level, verdict, ReasonOf(node, verdict), node?["why"]?.GetValue<string>() ?? string.Empty)
            : null;

    /// <summary>
    /// The check's reason as the judge named it. A check that did not pass without one this engine knows reads as
    /// <see cref="ReasonCode.Unknown"/>: shown without a Next line, deciding nothing.
    /// </summary>
    private static ReasonCode ReasonOf(JsonNode? node, CheckVerdict verdict) =>
        verdict is CheckVerdict.Pass or CheckVerdict.NotApplicable
            ? ReasonCode.None
            : ReasonCodeNames.Parse(node?["reason"] is JsonValue value && value.TryGetValue(out string? name) ? name : null);

    private static JsonObject Missing(string head, string why, CheckVerdict verdict = CheckVerdict.NotEarned)
    {
        string name = CheckVerdictNames.ToName(verdict);
        string reason = ReasonCodeNames.ToName(verdict == CheckVerdict.NotEarned ? ReasonCode.JudgeDidNotFinish : ReasonCode.EvidenceInvalid)!;
        return new JsonObject
        {
            ["schema"] = CiEvidence.Schema,
            ["head"] = head,
            ["judge"] = "missing",
            ["checks"] = new JsonArray(new JsonObject { ["id"] = "green", ["context"] = "gates", ["verdict"] = name, ["requirement"] = "required", ["why"] = why, ["reason"] = reason },
                new JsonObject { ["id"] = "red", ["context"] = "lockflight/judge", ["verdict"] = name, ["requirement"] = "required", ["why"] = why, ["reason"] = reason }),
            ["contexts"] = new JsonObject { [StatusContextNames.ToName(StatusContext.Gates)] = name, [StatusContextNames.ToName(StatusContext.Judge)] = name },
            ["verdict"] = name,
        };
    }
}
