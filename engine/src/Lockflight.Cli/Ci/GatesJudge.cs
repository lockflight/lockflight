using System.Text.Json.Nodes;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The <c>pr</c> gates at head (step P5), run in the judge's worktree with the base policy laid over it and the merge base as
/// <c>--base</c> (and, in a unit repository, the workspace shell's clone as <c>--shell</c>), exactly as
/// <c>lockflight gate run --profile pr</c> runs them; their result file (<c>lockflight.result/v1</c>, read by step id) is embedded in
/// the evidence.
/// </summary>
internal static class GatesJudge
{
    public static async Task<(CheckVerdict Verdict, string Why, JsonNode? Result, string? TestStep)> RunAsync(
        JudgeWorktree worktree, JudgePlan plan, CliContext context, string resultDirectory, CancellationToken cancellationToken)
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
        ExitCode exitCode = await new GateCommand(gates).RunAsync(new CommandArguments(command, [.. args]), cancellationToken);
        JsonNode? result = File.Exists(resultPath) ? JsonNode.Parse(await File.ReadAllTextAsync(resultPath, cancellationToken)) : null;
        if (result?["schema"]?.GetValue<string>() != GateResultWriter.Schema || result["steps"] is not JsonArray steps)
        {
            string tail = string.Join(' ', error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(3));
            return (CheckVerdict.Error, $"the gates did not produce a result (exit {(int)exitCode}): {tail}", null, null);
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
        return (verdict, summary.Length == 0 ? "no gate step ran" : summary, result, testStep);
    }
}
