using System.Runtime.InteropServices;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Ci;

/// <summary>
/// <c>lockflight ci</c>: judges a pull request's commits in CI. It takes the head, the target branch's tip and the source branch from
/// flags the platform's template fills in, reads the policy from the target branch, finds the commits that carry a
/// <c>Lockflight-Red</c> trailer and classifies the change, then writes <c>evidence.json</c> and exits with the verdict. A unit
/// repository of a workspace shell is judged with the shell's policy and specs too (<c>--shell</c>, a read-only clone). Nothing the
/// pull request says about itself is trusted: the trailer only says where to look. <c>--plan</c> prints the plan without building.
/// </summary>
internal sealed class CiCommand(CliContext context) : ICommand
{
    public const string PlanFlag = "--plan";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        DateTimeOffset startedAt = context.Time.GetUtcNow();
        JudgeInputsReading reading = await JudgeInputsReader.ReadAsync(options, context.WorkingDirectory, cancellationToken);
        if (reading.Inputs is not JudgeInputs inputs)
        {
            await context.Error.WriteLineAsync($"lockflight ci: {reading.Error}");
            return ExitCode.Config;
        }

        if (inputs.Platform != JudgePlatform.Local && await JudgeAttributes.NeutraliseAsync(inputs.Root, cancellationToken) is string attributesError)
        {
            await context.Error.WriteLineAsync($"lockflight ci: {attributesError}");
            return ExitCode.Error;
        }

        JudgePlanning planning = await JudgePlanner.PlanAsync(inputs, context.Environment, cancellationToken);
        if (planning.Plan is not JudgePlan plan)
        {
            await context.Error.WriteLineAsync($"lockflight ci: {planning.Error}");
            return planning.Failure;
        }

        if (plan.Policy.Manifest is null)
        {
            // Defaults would judge with a policy nobody chose, and every verdict would be about the wrong thing.
            await context.Error.WriteLineAsync(
                plan.Inputs.Event == JudgeEvent.PullRequest
                    ? $"lockflight ci: the target branch ({CommitIds.Short(plan.Policy.Commit)}) has no {Manifest.FileName}, so there is no policy to judge with; "
                      + "commit 'lockflight init --ci-only' (or 'lockflight init') to the target branch first, then branch from it or merge it in"
                    : plan.Inputs.Event == JudgeEvent.MergeGroup
                    ? $"lockflight ci: the merge queue's target branch ({CommitIds.Short(plan.Policy.Commit)}) has no {Manifest.FileName}, so there is no policy to judge with; "
                      + "commit 'lockflight init --ci-only' (or 'lockflight init') to the target branch first"
                    : $"lockflight ci: {CommitIds.Short(plan.Policy.Commit)}, whose policy judges this push, has no {Manifest.FileName}, so there is no policy "
                      + "to judge with; the judge runs from the first push after 'lockflight init --ci-only' is on the branch");
            return ExitCode.Config;
        }

        ShellPolicyReading shell = await JudgeShell.ReadAsync(plan, cancellationToken);
        if (shell.Error is string shellError)
        {
            await context.Error.WriteLineAsync($"lockflight ci: {shellError}");
            return ExitCode.Config;
        }

        if (shell.Policy is ShellPolicy shellPolicy)
        {
            // The class and its check levels are routed again under the shell's policy merged with the unit's.
            planning = await JudgePlanner.PlanAsync(inputs, context.Environment, shellPolicy, cancellationToken);
            if (planning.Plan is not JudgePlan shellPlan)
            {
                await context.Error.WriteLineAsync($"lockflight ci: {planning.Error}");
                return planning.Failure;
            }

            plan = shellPlan;
        }

        plan = plan with { Shell = shell.Policy };
        if (options.Flag(PlanFlag))
        {
            await context.Output.WriteAsync(JsonFiles.Format(CiEvidence.Plan(plan)));
            return ExitCode.Pass;
        }

        JudgeFindings findings = await JudgeExecutor.RunAsync(plan, context, cancellationToken);
        findings = findings with
        {
            Approvals = await ApprovalsJudge.DecideAsync(plan, findings, cancellationToken),
            SpecDiff = await SpecDiffReader.ReadAsync(plan, cancellationToken),
        };
        JudgeRun run = new(plan, findings, JudgeChecks.Decide(plan, findings), startedAt, context.Time.GetUtcNow());
        string path = Path.Combine(inputs.EvidenceDirectory, inputs.Platform == JudgePlatform.Local ? CiEvidence.LocalFileName : CiEvidence.FileName);
        string? engineSha256 = await JudgeEngineHash.ReadAsync(plan, ToolPin.RunningVersion, RuntimeInformation.RuntimeIdentifier, cancellationToken);
        JsonFiles.Write(path, CiEvidence.Build(run, ToolPin.RunningVersion, RuntimeInformation.RuntimeIdentifier, engineSha256));
        await WriteSummaryAsync(run, path);
        return CheckVerdictNames.ToExitCode(run.Verdict);
    }

    private async Task WriteSummaryAsync(JudgeRun run, string path)
    {
        JudgePlan plan = run.Plan;
        await context.Output.WriteLineAsync(
            $"{CheckVerdictNames.ToName(run.Verdict)} head {CommitIds.Short(plan.Inputs.Head)}, class {ChangeClassNames.ToName(plan.Class.Classification.Class)} ({plan.Class.Source}), "
            + $"{plan.Reds.Cycles.Count} red commits, policy from {CommitIds.Short(plan.Policy.Commit)}");
        foreach (StatusContext statusContext in Enum.GetValues<StatusContext>())
        {
            await context.Output.WriteLineAsync($"  {StatusContextNames.ToName(statusContext),-17} {CheckVerdictNames.ToName(run.VerdictOf(statusContext))}");
        }

        foreach (CheckResult check in run.Checks)
        {
            await context.Output.WriteLineAsync($"  {CheckVerdictNames.ToName(check.Verdict),-10} {CheckIdNames.ToName(check.Id),-22} {CheckLevelNames.ToName(check.Level),-8} {check.Why}");
        }

        foreach (RedCommit red in plan.Reds.All.Where(red => !red.IsCycle))
        {
            string why = red.Malformed ?? "superseded by a later commit with the same label";
            await context.Output.WriteLineAsync($"  note: {CommitIds.Short(red.Commit)} is not a red: {why}");
        }

        if (plan.Reds.LabelsOutOfOrder)
        {
            await context.Output.WriteLineAsync("  note: the cycle labels are out of order; the commits' order decides the cycle order");
        }

        if (plan.Ignored.Count > 0)
        {
            await context.Output.WriteLineAsync($"  ignored (present, not read): {string.Join(", ", plan.Ignored)}");
        }

        await context.Output.WriteLineAsync($"Evidence: {path}");
    }
}
