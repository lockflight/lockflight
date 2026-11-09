using System.Runtime.InteropServices;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Processes.Survivors;
using Lockflight.Cli.Reasons;
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

    /// <summary>The longest command line of a process left running the summary shows; the evidence keeps more.</summary>
    private const int MaxCommandShown = 200;

    /// <summary>GitHub Actions sets it to <c>github-hosted</c> on its own runners, a fresh machine per job; no other platform sets it.</summary>
    private const string RunnerEnvironmentVariable = "RUNNER_ENVIRONMENT";
    private const string GitHubHosted = "github-hosted";

    /// <summary>A container id as <c>docker ps</c> shows it.</summary>
    private const int ShortContainerId = 12;

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        // Before the judge starts anything, so every process it starts, and every one those leave running, is in the sweep's reach.
        using ProcessSweep sweep = ProcessSweep.Start(context.ProcessScope(JudgeInputsReader.InCi(options)));
        DateTimeOffset startedAt = context.Time.GetUtcNow();
        JudgeInputsReading reading = await JudgeInputsReader.ReadAsync(options, context.WorkingDirectory, cancellationToken);
        if (reading.Inputs is not JudgeInputs inputs)
        {
            await context.Error.WriteLineAsync($"lockflight ci: {reading.Error}");
            return ExitCode.Config;
        }

        await WatchContainersAsync(sweep, inputs, cancellationToken);
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
        string? engineSha256 = await JudgeEngineHash.ReadAsync(plan, ToolPin.RunningVersion, RuntimeInformation.RuntimeIdentifier, cancellationToken);
        findings = findings with { Processes = await sweep.FinishAsync(cancellationToken) };
        JudgeRun run = new(plan, findings, JudgeChecks.Decide(plan, findings), startedAt, context.Time.GetUtcNow());
        string path = Path.Combine(inputs.EvidenceDirectory, inputs.Platform == JudgePlatform.Local ? CiEvidence.LocalFileName : CiEvidence.FileName);
        JsonFiles.Write(path, CiEvidence.Build(run, ToolPin.RunningVersion, RuntimeInformation.RuntimeIdentifier, engineSha256));
        await WriteSummaryAsync(run, path);
        if (inputs.Platform == JudgePlatform.Local && run.Verdict == CheckVerdict.Pass
            && await new FirstGreen(inputs.Root, context.Time).ReportPassAsync(cancellationToken) is string firstGreen)
        {
            await context.Output.WriteLineAsync(firstGreen);
        }

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
            if (check.Level != CheckLevel.Off && check.Verdict is not (CheckVerdict.Pass or CheckVerdict.NotApplicable) && ReasonCatalogue.ConsoleLine(check.Reason) is string next)
            {
                await context.Output.WriteLineAsync($"             {next}");
            }
        }

        if (run.Findings.Tampering is { New.Count: > 0 } tampering)
        {
            await context.Output.WriteLineAsync($"  host-tampering: {HostTamperingJudge.AcceptAdvice} Each new finding and its entry:");
            foreach (TamperingFinding finding in tampering.New)
            {
                await context.Output.WriteLineAsync($"    {Printable(HostTamperingJudge.Describe(finding))}");
                await context.Output.WriteLineAsync($"      {HostTamperingJudge.BaselineEntry(finding)}");
            }
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

        if (run.Findings.Processes is ProcessesReport processes)
        {
            await WriteProcessesAsync(processes);
        }

        IReadOnlyList<string> held = run.Findings.Processes?.OutputHeld ?? HeldOutputs.Snapshot();
        foreach (string command in held)
        {
            await context.Output.WriteLineAsync(
                $"  note: '{Printable(command)}' exited, but a process it started kept its output open; Lockflight stopped waiting for it after "
                + $"{ProcessRunner.OutputDrainTime.TotalSeconds:0} s without new output");
        }

        if (held.Count > 0 && ReasonCatalogue.ConsoleLine(ReasonCode.SurvivorOutputHeld) is string heldNext)
        {
            await context.Output.WriteLineAsync($"        {heldNext}");
        }

        if (plan.Ignored.Count > 0)
        {
            await context.Output.WriteLineAsync($"  ignored (present, not read): {string.Join(", ", plan.Ignored)}");
        }

        await context.Output.WriteLineAsync($"Evidence: {path}");
    }

    private async Task WriteProcessesAsync(ProcessesReport processes)
    {
        if (processes.Mechanism == SweepMechanism.None)
        {
            await context.Output.WriteLineAsync($"  note: processes left running were not checked: {processes.Note}");
        }
        else if (processes.Note is string note)
        {
            await context.Output.WriteLineAsync($"  note: {note}");
        }

        // A process that exited by itself before the sweep (an MSBuild worker node a moment after its build) was only reaped.
        Survivor[] running = [.. processes.Survivors.Where(survivor => survivor.Process.Outcome != SurvivorOutcome.Exited)];
        int exited = processes.Survivors.Count - running.Length;
        if (processes.Mechanism != SweepMechanism.None || running.Length > 0)
        {
            await context.Output.WriteLineAsync(
                $"  processes: {running.Length + processes.More} left running, {exited} exited by themselves, found by {processes.Sweeps} checks "
                + $"({SweepMechanismNames.ToName(processes.Mechanism)}); "
                + (processes.NotStopped > 0 ? $"{processes.NotStopped} could not be stopped, so green and gates are ERROR" : "each was stopped, which does not change the verdict"));
        }

        foreach (Survivor survivor in running)
        {
            string where = survivor.Process is { Kind: SurvivorKind.Container, Container: string id }
                ? $"container {Printable(id[..Math.Min(ShortContainerId, id.Length)])}, image '{Printable(Cut(survivor.Process.Image))}'"
                : $"pid {survivor.Process.Pid}";
            await context.Output.WriteLineAsync(
                $"    {SurvivorOutcomeNames.ToName(survivor.Process.Outcome)}: '{Printable(Cut(survivor.Process.Command))}' ({where}, "
                + $"{SweepRegionNames.ToName(survivor.Region)}, after '{Printable(survivor.After)}')");
        }
    }

    /// <summary>
    /// Lists the Docker containers that exist before the judge starts anything that builds, so the ones the pull request's code creates
    /// are found at the end of each part of the run; they are removed only on a GitHub-hosted runner (<see cref="RemovesContainers"/>).
    /// </summary>
    private async Task WatchContainersAsync(ProcessSweep sweep, JudgeInputs inputs, CancellationToken cancellationToken)
    {
        if (sweep.Mechanism == SweepMechanism.None)
        {
            // No process sweep (a test host, or a platform without one): the containers are left to the job's end too.
            return;
        }

        if (await DockerContainers.WatchAsync(RemovesContainers(inputs.Platform, context.Environment), cancellationToken) is DockerContainers containers)
        {
            sweep.Watch(containers);
        }
    }

    /// <summary>
    /// Whether the judge removes the containers the pull request's code created: only on a GitHub-hosted runner, a machine of the job's
    /// own. Elsewhere (a local run, a self-hosted runner, another platform's agent) the Docker daemon may be shared with other jobs, whose
    /// containers must not be removed, so they are listed and left.
    /// </summary>
    internal static bool RemovesContainers(JudgePlatform platform, EnvironmentReader environment) =>
        platform != JudgePlatform.Local && environment.Get(RunnerEnvironmentVariable) == GitHubHosted;

    private static string Cut(string text) => text.Length <= MaxCommandShown ? text : $"{text[..MaxCommandShown]}…";

    /// <summary>A name from the pull request's code on one line, without control characters that a terminal would act on.</summary>
    private static string Printable(string text) => new([.. text.ReplaceLineEndings(" ").Where(character => !char.IsControl(character))]);
}
