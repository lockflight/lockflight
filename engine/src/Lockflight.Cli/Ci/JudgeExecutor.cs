using System.Text.Json.Nodes;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.WindowsTargets;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Processes.Survivors;
using Lockflight.Cli.Reasons;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Runs the steps of a plan that build: opens the judge's worktree (in <c>LOCKFLIGHT_TEMP</c> when the template sets it, else
/// locally in the repository's git directory, else a new folder of its own in the system's temporary folder, readable by this user
/// only and removed afterwards), scans the head for secrets before anything is
/// built there, runs the head's suite (P1), lists the base (P2) and re-runs the red commits (P3), then judges green over what they
/// found, and removes the worktree again whatever happened. A head whose build includes a test project that needs Windows is not
/// built on another OS (<see cref="WindowsRunner"/>). The .NET build servers are shut down after each phase that builds.
/// </summary>
internal static class JudgeExecutor
{
    private const string LocalFolder = "lockflight";

    public static async Task<JudgeFindings> RunAsync(JudgePlan plan, CliContext context, CancellationToken cancellationToken)
    {
        EnvironmentReader environment = context.Environment;
        TimeProvider time = context.Time;
        bool judgesReds = plan.Levels[CheckId.Red] != CheckLevel.Off && !plan.Reds.OverCap && plan.Reds.Cycles.Count > 0 && plan.MergeBase is not null;
        if (plan.Levels[CheckId.Green] == CheckLevel.Off)
        {
            return JudgeFindings.None;
        }

        string root = plan.Inputs.Root;
        JudgeBudget budget = new(TimeSpan.FromMinutes(plan.Policy.Ci.TimeoutMinutes), time);
        TimeSpan gitLimit = TimeSpan.FromMinutes(5);
        (string parent, bool ownParent) = await ParentAsync(plan.Inputs, environment, cancellationToken);
        if (await JudgeWorktree.OpenAsync(root, parent, plan.Inputs.Head, gitLimit, cancellationToken) is not JudgeWorktree worktree)
        {
            RemoveOwnParent(parent, ownParent);
            string why = $"git could not add a worktree under {parent}";
            GreenJudgement green = new(CheckVerdict.Error, ReasonCode.ToolError, why, 0, 0, [why], [], new Dictionary<string, string>(), []);
            return new JudgeFindings(green, judgesReds ? [.. plan.Reds.Cycles.Select(red => new CycleJudgement(red, TreeOutcome.Broken, [], new Dictionary<string, RedVerdict>(), CheckVerdict.Error, ReasonCode.ToolError, why))] : null, []);
        }

        try
        {
            // The worktree is opened without a checkout, so off Windows the head's project files are checked out to be read.
            if (!OperatingSystem.IsWindows() && await worktree.MoveToAsync(plan.Inputs.Head, plan.Policy.Commit, clean: true, cancellationToken) is null
                && WindowsRunner.Why(worktree.Root, BuildScope.Of(plan.Policy.Manifest, worktree.Root), onWindows: false) is string needsWindows)
            {
                return JudgeFindings.None with { NotBuilt = (CheckVerdict.Error, ReasonCode.NeedsWindowsRunner, needsWindows), HeadCommit = plan.Inputs.Head };
            }

            GateStep secrets = await SecretsJudge.ScanAsync(worktree, plan, context, cancellationToken);
            TreeBuilder trees = new(worktree, plan.Policy, root, budget, time);
            if (await EnterAsync(SweepRegion.Head, plan, cancellationToken) is JudgeFindings stoppedHead)
            {
                return stoppedHead;
            }

            HeadRun head = await HeadRunner.RunAsync(trees, plan, cancellationToken);
            await BuildServers.ShutdownAsync(worktree.Root, cancellationToken);
            if (await EnterAsync(SweepRegion.Base, plan, cancellationToken) is JudgeFindings stoppedBase)
            {
                return stoppedBase;
            }

            TreeListing? baseListing = plan.MergeBase is string mergeBase ? await trees.ListAsync(mergeBase, "base", fresh: false, cancellationToken) : null;
            BaseSkipCheck baseSkips = await BaseSkipsAsync(trees, head, baseListing, cancellationToken);
            await BuildServers.ShutdownAsync(worktree.Root, cancellationToken);
            IReadOnlySet<string> structuralIds = await StructuralJudge.IdsAsync(plan, cancellationToken);
            if (await EnterAsync(SweepRegion.Red, plan, cancellationToken) is JudgeFindings stoppedRed)
            {
                return stoppedRed;
            }

            IReadOnlyList<CycleJudgement>? cycles = judgesReds ? await new RedCommitJudge(trees, time).JudgeAsync(plan, structuralIds, cancellationToken) : null;
            await BuildServers.ShutdownAsync(worktree.Root, cancellationToken);
            if (await EnterAsync(SweepRegion.Rechecks, plan, cancellationToken) is JudgeFindings stoppedRechecks)
            {
                return stoppedRechecks;
            }

            GreenJudgement greenAtHead = baseListing is { AsBaseline: null }
                ? new GreenJudgement(CheckVerdict.NotEarned, CheckReasons.BaseNotListed(baseListing.Outcome), $"the base {CommitIds.Short(plan.MergeBase!)} could not be listed to compare with ({TreeOutcomeNames.ToName(baseListing.Outcome)}: {baseListing.Detail})", 0, 0, [], [], new Dictionary<string, string>(), [])
                : GreenJudge.Decide(head, baseListing?.AsBaseline, baseSkips, cycles ?? []);
            IReadOnlyList<SinceRedTest>? sinceRed = cycles is not null && plan.Levels[CheckId.TestsSinceRed] != CheckLevel.Off
                ? await new SinceRedJudge(trees, time).JudgeAsync(plan, head.Listing, head.Results, cycles, cancellationToken)
                : null;
            IReadOnlyList<ExistingChangedTest>? existingChanged = baseListing is not null && plan.Levels[CheckId.TestsExistingChanged] != CheckLevel.Off
                ? await new ExistingChangedJudge(trees, time).JudgeAsync(plan, head.Listing, baseListing, cancellationToken)
                : null;
            TamperingJudgement? tampering = plan.Levels[CheckId.TestsHostTampering] != CheckLevel.Off
                ? await HostTamperingJudge.DecideAsync(plan, head.Listing, baseListing, cycles ?? [], cancellationToken)
                : null;
            TraceJudgement? trace = plan.Levels[CheckId.Trace] != CheckLevel.Off
                ? await TraceJudge.DecideAsync(plan, head.Listing.Inventory, baseListing?.Inventory, cycles ?? [], sinceRed, greenAtHead, await new StructuralJudge(trees, time).JudgeAsync(plan, head, baseListing?.AsBaseline, structuralIds, cancellationToken), cancellationToken)
                : null;
            await BuildServers.ShutdownAsync(worktree.Root, cancellationToken);
            if (await EnterAsync(SweepRegion.Gates, plan, cancellationToken) is JudgeFindings stoppedGates)
            {
                return stoppedGates;
            }

            (CheckVerdict Verdict, ReasonCode Reason, string Why, JsonNode? Result, string? TestStep) gates = !budget.TryStart("gates")
                ? (CheckVerdict.NotEarned, ReasonCode.CiBudgetExhausted, budget.Why(trees.Builds.Count), null, null)
                : await worktree.MoveToAsync(plan.Inputs.Head, plan.Policy.Commit, clean: false, cancellationToken) is string notMoved
                ? (CheckVerdict.Error, ReasonCode.ToolError, notMoved, null, null)
                : await GatesJudge.RunAsync(worktree, plan, context, secrets, plan.Inputs.EvidenceDirectory, budget, cancellationToken);
            // Only when the base had none either: a pull request that turns the test projects into ones red/green cannot run must
            // not lose the check that nothing was deleted.
            bool baseHadNone = baseListing is null || baseListing.Outcome == TreeOutcome.NoTestApps;
            if (head.Listing.Outcome == TreeOutcome.NoTestApps && baseHadNone && gates.TestStep is string testStep)
            {
                // Without a test project red/green can run, the gates' own test step judges the suite at head.
                greenAtHead = greenAtHead with
                {
                    Verdict = testStep switch { "PASS" => CheckVerdict.Pass, "FAIL" => CheckVerdict.Fail, "ERROR" => CheckVerdict.Error, _ => CheckVerdict.NotEarned },
                    Reason = testStep switch { "PASS" => ReasonCode.None, "FAIL" => ReasonCode.TestsFailed, "ERROR" => ReasonCode.TestRunBroken, _ => ReasonCode.NoTestsRan },
                    Why = $"no test project red/green can run (xUnit, NUnit or MSTest); the gates' test step judged the suite: {testStep}",
                };
            }

            return new JudgeFindings(greenAtHead, cycles, trees.Builds)
            {
                Trace = trace,
                Gates = (gates.Verdict, gates.Reason, gates.Why, gates.Result),
                Tampering = tampering,
                HeadFingerprints = head.Listing.Fingerprints,
                SinceRed = sinceRed,
                ExistingChanged = existingChanged,
                HeadCommit = plan.Inputs.Head,
                Budget = budget,
            };
        }
        finally
        {
            await BuildServers.ShutdownAsync(worktree.Root, CancellationToken.None);
            await worktree.RemoveAsync();
            RemoveOwnParent(parent, ownParent);
        }
    }

    /// <summary>
    /// Ends one part of the run and starts the next (<see cref="ProcessSweep.EnterAsync"/>). When a sweep has found a process or
    /// container it could not stop, nothing read from here on can be trusted, so the judge runs no further part: every check that
    /// builds is ERROR with <see cref="ReasonCode.SurvivorNotStopped"/>. Null when the run goes on.
    /// </summary>
    private static async Task<JudgeFindings?> EnterAsync(SweepRegion region, JudgePlan plan, CancellationToken cancellationToken)
    {
        await ProcessSweep.EnterAsync(region, cancellationToken);
        return ProcessSweep.Current is { NotStopped: > 0 } sweep
            ? JudgeFindings.None with { NotBuilt = (CheckVerdict.Error, ReasonCode.SurvivorNotStopped, NotStoppedWhy(sweep.NotStopped)), HeadCommit = plan.Inputs.Head }
            : null;
    }

    /// <summary>The detail line of a check made ERROR by processes or containers the sweeps could not stop.</summary>
    public static string NotStoppedWhy(int count) =>
        $"{count} {(count == 1 ? "process or container" : "processes or containers")} the pull request's code started could not be stopped "
        + "(another user's, typically started through sudo), so nothing the judge read after it started can be trusted; the evidence's processes lists them";

    /// <summary>
    /// Runs the base tests head skipped at run time on the base's build, still in the worktree after the base was listed, within the
    /// budget and the per-process limit, and matches their skipped rows with head's by skip message. A guardrail: a test that skips
    /// for the same reason on both sides (no Docker on the runner) is not a new skip; one that skips with another message is.
    /// </summary>
    private static async Task<BaseSkipCheck> BaseSkipsAsync(TreeBuilder trees, HeadRun head, TreeListing? baseListing, CancellationToken cancellationToken)
    {
        IReadOnlyList<TestCase> again = GreenJudge.SkippedAtRunTime(head, baseListing?.Inventory);
        if (again.Count == 0 || baseListing?.Inventory is null || head.Results is not IReadOnlyList<ExecutedTest> headResults)
        {
            return BaseSkipCheck.None;
        }

        string[] keys = [.. again.Select(test => test.Key)];
        if (!trees.Budget.TryStart("base run of the tests skipped at run time"))
        {
            return new BaseSkipCheck(new Dictionary<string, int>(StringComparer.Ordinal), keys, trees.Budget.Why(trees.Builds.Count));
        }

        TestWorkspace workspace = baseListing.Workspace;
        IReadOnlyList<ExecutedTest>? results = await workspace.RunAsync(again, trees.ProcessLimit, cancellationToken);
        return results is null || workspace.AbnormalExit is not null
            ? new BaseSkipCheck(new Dictionary<string, int>(StringComparer.Ordinal), keys, $"the runner did not finish on the base ({workspace.AbnormalExit ?? "no report"})")
            : GreenJudge.CompareSkips(headResults, results, keys);
    }

    /// <summary>
    /// The folder the worktree is added under, and whether the judge made it for this run. In the system's temporary folder a fixed
    /// name could be another user's folder on a shared host, so the judge makes one of its own there (<see cref="Directory.CreateTempSubdirectory"/>
    /// gives it a random name and, off Windows, access for this user only).
    /// </summary>
    private static async Task<(string Parent, bool Own)> ParentAsync(JudgeInputs inputs, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        if (environment.Get(EnvironmentVariables.Temp) is { Length: > 0 } temp)
        {
            return (Path.Combine(temp, LocalFolder), false);
        }

        if (inputs.Platform == JudgePlatform.Local)
        {
            ProcessResult common = await ProcessRunner.RunAsync("git", ["rev-parse", "--git-common-dir"], inputs.Root, cancellationToken);
            if (common.ExitCode == 0 && common.Output.Trim() is { Length: > 0 } gitDirectory)
            {
                return (Path.Combine(Path.GetFullPath(gitDirectory, inputs.Root), LocalFolder), false);
            }
        }

        return (Directory.CreateTempSubdirectory($"{LocalFolder}-").FullName, true);
    }

    private static void RemoveOwnParent(string parent, bool own)
    {
        try
        {
            if (own && Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not empty (a worktree git could not remove): it stays in the temporary folder, as the worktree itself would.
        }
    }
}
