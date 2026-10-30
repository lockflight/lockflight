using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Ci;

/// <summary>
/// <c>lockflight ci report</c>: the report job's step. It reads the judge's evidence and the review job's result as data (never
/// running anything from them), joins the review into its check, recomputes both status contexts, and writes the evidence of record
/// (<c>verdict.json</c>), the PR comment (<c>comment.md</c>, within GitHub's limit) and the job summary (<c>summary.md</c>). The exit
/// code is the verdict of <c>--context</c> (<c>gates</c> or <c>lockflight/judge</c>), else of the whole run. A missing judge
/// artifact is NOT-EARNED ("the judge did not finish"); evidence about another head is ERROR; a review about another head is ignored.
/// </summary>
internal sealed class CiReportCommand(CliContext context) : ICommand
{
    public const string EvidenceOption = "--evidence";
    public const string ReviewOption = "--review";
    public const string HeadOption = "--head";
    public const string ContextOption = "--context";
    public const string OutOption = "--out";

    public const string VerdictFileName = "verdict.json";
    public const string CommentFileName = "comment.md";
    public const string SummaryFileName = "summary.md";

    /// <summary>The review job's result file the report joins: <c>lockflight.ci-review/v1</c>.</summary>
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

        JsonObject verdict = Join(Read(Path.GetFullPath(Path.Combine(evidenceDirectory, CiEvidence.FileName), context.WorkingDirectory)), head,
            options.Value(ReviewOption) is string review ? Read(Path.GetFullPath(Path.Combine(review, ReviewFileName), context.WorkingDirectory)) : null);
        string output = Path.GetFullPath(outDirectory, context.WorkingDirectory);
        JsonFiles.Write(Path.Combine(output, VerdictFileName), verdict);
        await File.WriteAllTextAsync(Path.Combine(output, CommentFileName), CommentRenderer.Render(verdict, CommentRenderer.MaxCommentLength), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(output, SummaryFileName), CommentRenderer.Render(verdict, int.MaxValue), cancellationToken);

        string shown = only is StatusContext statusContext
            ? verdict["contexts"]?[StatusContextNames.ToName(statusContext)]?.GetValue<string>() ?? "ERROR"
            : verdict["verdict"]?.GetValue<string>() ?? "ERROR";
        await context.Output.WriteLineAsync($"{shown} {(only is StatusContext named ? StatusContextNames.ToName(named) : "lockflight ci")} for head {CommitIds.Short(head)}");
        if (CommentRenderer.Next(verdict) is string next)
        {
            await context.Output.WriteLineAsync($"Next: {TerminalText.Clean(next.ReplaceLineEndings(" "))}");
        }

        return CheckVerdictNames.ToExitCode(CheckVerdictNames.Parse(shown) ?? CheckVerdict.Error);
    }

    /// <summary>The evidence with the review joined and the contexts and verdict recomputed from the checks.</summary>
    public static JsonObject Join(JsonObject? evidence, string head, JsonObject? review)
    {
        if (evidence is null || evidence["schema"]?.GetValue<string>() != CiEvidence.Schema)
        {
            return Missing(head, "the judge did not finish: its evidence artifact is missing or not lockflight.ci-evidence/v1 (cancelled, killed at the platform's time limit, or failed to upload)");
        }

        if (evidence["head"]?.GetValue<string>() != head)
        {
            return Missing(head, $"the judge's evidence is about another head ({TerminalText.Clean(evidence["head"]?.GetValue<string>() ?? "none")}), not {head}: an artifact from another run", CheckVerdict.Error);
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

        evidence["review"] = review is null ? new JsonObject { ["state"] = "skipped" } : review.DeepClone();
        evidence["checks"] = new JsonArray([.. checks.Select(check => (JsonNode)new JsonObject
        {
            ["id"] = CheckIdNames.ToName(check.Id),
            ["context"] = StatusContextNames.ToName(check.Context),
            ["verdict"] = CheckVerdictNames.ToName(check.Verdict),
            ["requirement"] = CheckLevelNames.ToName(check.Level),
            ["why"] = CiEvidence.Cap(check.Why),
        })]);
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

        return evidence;
    }

    /// <summary>The review job's verdict as the review check, or SKIPPED: no review ran, or it reviewed another head.</summary>
    private static CheckResult Review(CheckResult check, JsonObject? review, string head)
    {
        if (review is null)
        {
            return check with { Verdict = CheckVerdict.Skipped, Why = "no review ran (no API key configured, a fork, or the review job was skipped)" };
        }

        if (review["head"]?.GetValue<string>() != head)
        {
            return check with { Verdict = CheckVerdict.Skipped, Why = "SKIPPED (stale): the review is about another head" };
        }

        CheckVerdict verdict = CheckVerdictNames.Parse(review["verdict"]?.GetValue<string>() ?? string.Empty) ?? CheckVerdict.Error;
        return check with { Verdict = verdict, Why = review["why"]?.GetValue<string>() ?? CheckVerdictNames.ToName(verdict) };
    }

    private static CheckResult? Parse(JsonNode? node) =>
        CheckIdNames.Parse(node?["id"]?.GetValue<string>() ?? string.Empty) is CheckId id
        && CheckLevelNames.Parse(node?["requirement"]?.GetValue<string>() ?? string.Empty) is CheckLevel level
        && CheckVerdictNames.Parse(node?["verdict"]?.GetValue<string>() ?? string.Empty) is CheckVerdict verdict
            ? new CheckResult(id, level, verdict, node?["why"]?.GetValue<string>() ?? string.Empty)
            : null;

    private static JsonObject Missing(string head, string why, CheckVerdict verdict = CheckVerdict.NotEarned)
    {
        string name = CheckVerdictNames.ToName(verdict);
        return new JsonObject
        {
            ["schema"] = CiEvidence.Schema,
            ["head"] = head,
            ["judge"] = "missing",
            ["checks"] = new JsonArray(new JsonObject { ["id"] = "green", ["context"] = "gates", ["verdict"] = name, ["requirement"] = "required", ["why"] = why },
                new JsonObject { ["id"] = "red", ["context"] = "lockflight/judge", ["verdict"] = name, ["requirement"] = "required", ["why"] = why }),
            ["contexts"] = new JsonObject { [StatusContextNames.ToName(StatusContext.Gates)] = name, [StatusContextNames.ToName(StatusContext.Judge)] = name },
            ["verdict"] = name,
        };
    }

    private static JsonObject? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
