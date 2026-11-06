using System.Text.Json.Nodes;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The <c>pr</c> gates at head (step P5), run in the judge's worktree with the base policy laid over it and the merge base as
/// <c>--base</c> (and, in a unit repository, the workspace shell's clone as <c>--shell</c>), exactly as
/// <c>lockflight gate run --profile pr</c> runs them, except that the secrets step reports the scan the judge ran before it
/// built anything; their result file (<c>lockflight.result/v1</c>, read by step id) is embedded in the evidence.
/// </summary>
internal static class GatesJudge
{
    public static async Task<(CheckVerdict Verdict, ReasonCode Reason, string Why, JsonNode? Result, string? TestStep)> RunAsync(
        JudgeWorktree worktree, JudgePlan plan, CliContext context, GateStep secrets, string resultDirectory, JudgeBudget budget, CancellationToken cancellationToken)
    {
        string resultPath = Path.Combine(resultDirectory, "gates-result.json");
        if (File.Exists(resultPath))
        {
            File.Delete(resultPath);
        }

        using StringWriter output = new();
        using StringWriter error = new();
        CliContext gates = context with { Input = TextReader.Null, Output = output, Error = error, WorkingDirectory = worktree.Root, OutputIsTerminal = false, RawOutput = null };
        List<string> args = ["--profile", "pr", "--result", resultPath];
        if (plan.MergeBase is string mergeBase)
        {
            args.AddRange(["--base", mergeBase]);
        }

        if (plan.Shell is { } shell)
        {
            args.AddRange([GateCommand.ShellOption, shell.Directory]);
        }

        CommandDescriptor command = CommandRegistry.Match(["gate", "run"])!;
        // The gates run under the judge's budget too: they are stopped before the reserve, so the evidence is still written.
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget.ProcessLimit(TimeSpan.FromHours(24)));
        ExitCode exitCode;
        try
        {
            exitCode = await new GateCommand(gates, secrets, judgesAPullRequest: true).RunAsync(new CommandArguments(command, [.. args]), deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (CheckVerdict.NotEarned, ReasonCode.CiBudgetExhausted, "the gates did not finish within the CI budget (ci.timeoutMinutes on the target branch); raise it or split the pull request", null, null);
        }

        JsonNode? result = File.Exists(resultPath) ? JsonNode.Parse(await File.ReadAllTextAsync(resultPath, cancellationToken)) : null;
        if (result?["schema"]?.GetValue<string>() != GateResultWriter.Schema || result["steps"] is not JsonArray steps)
        {
            string tail = string.Join(' ', error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(3));
            return (CheckVerdict.Error, ReasonCode.ToolError, $"the gates did not produce a result (exit {(int)exitCode}): {tail}", null, null);
        }

        string summary = string.Join(" · ", steps.Select(step => $"{step?["name"]?.GetValue<string>()} {step?["verdict"]?.GetValue<string>()}"));
        string? testStep = steps.FirstOrDefault(step => step?["id"]?.GetValue<string>() == TestStep.StepName)?["verdict"]?.GetValue<string>();
        CheckVerdict verdict = result["verdict"]?.GetValue<string>() switch
        {
            "PASS" or "N/A" => CheckVerdict.Pass,
            "FAIL" => CheckVerdict.Fail,
            "NOT-EARNED" => CheckVerdict.NotEarned,
            _ => CheckVerdict.Error,
        };
        return (verdict, verdict == CheckVerdict.Pass ? ReasonCode.None : ReasonOf(steps), summary.Length == 0 ? "no gate step ran" : summary, result, testStep);
    }

    /// <summary>The reason of the first step that did not pass, as its result names it; <see cref="ReasonCode.GatesFailed"/> when none does.</summary>
    private static ReasonCode ReasonOf(JsonArray steps) =>
        steps.FirstOrDefault(step => step?["verdict"]?.GetValue<string>() is not ("PASS" or "N/A")) is JsonNode refused
        && refused["reason"] is JsonValue value && value.TryGetValue(out string? name) && ReasonCodeNames.Parse(name) is ReasonCode reason and not ReasonCode.Unknown
            ? reason
            : ReasonCode.GatesFailed;
}
