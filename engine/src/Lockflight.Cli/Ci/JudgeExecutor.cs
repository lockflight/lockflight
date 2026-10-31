using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Runs the steps of a plan that build: opens the judge's worktree (in <c>LOCKFLIGHT_TEMP</c> when the template sets it, else
/// locally in the repository's git directory, else the system's temporary folder), runs the head's suite (P1), lists the base (P2)
/// and re-runs the red commits (P3), then judges green over what they found, and removes the worktree again whatever happened.
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
        string parent = await ParentAsync(plan.Inputs, environment, cancellationToken);
        if (await JudgeWorktree.OpenAsync(root, parent, plan.Inputs.Head, gitLimit, cancellationToken) is not JudgeWorktree worktree)
        {
            string why = $"git could not add a worktree under {parent}";
            GreenJudgement green = new(CheckVerdict.Error, why, 0, 0, [why], [], new Dictionary<string, string>(), []);
            return new JudgeFindings(green, judgesReds ? [.. plan.Reds.Cycles.Select(red => new CycleJudgement(red, TreeOutcome.Broken, [], new Dictionary<string, RedVerdict>(), CheckVerdict.Error, why))] : null, []);
        }

        try
        {
            TreeBuilder trees = new(worktree, plan.Policy, root, budget, time);
            HeadRun head = await HeadRunner.RunAsync(trees, plan, cancellationToken);
            TreeListing? baseListing = plan.MergeBase is string mergeBase ? await trees.ListAsync(mergeBase, "base", fresh: false, cancellationToken) : null;
            BaseSkipCheck baseSkips = await BaseSkipsAsync(trees, head, baseListing, cancellationToken);
            IReadOnlyList<CycleJudgement>? cycles = judgesReds ? await new RedCommitJudge(trees, time).JudgeAsync(plan, cancellationToken) : null;
            GreenJudgement greenAtHead = baseListing is { AsBaseline: null }
                ? new GreenJudgement(CheckVerdict.NotEarned, $"the base {CommitIds.Short(plan.MergeBase!)} could not be listed to compare with ({TreeOutcomeNames.ToName(baseListing.Outcome)}: {baseListing.Detail})", 0, 0, [], [], new Dictionary<string, string>(), [])
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
                ? await TraceJudge.DecideAsync(plan, head.Listing.Inventory, cycles ?? [], sinceRed, greenAtHead, cancellationToken)
                : null;
            (CheckVerdict Verdict, string Why, JsonNode? Result, string? TestStep) gates = !budget.TryStart("gates")
                ? (CheckVerdict.NotEarned, budget.Why(trees.Builds.Count), null, null)
                : await worktree.MoveToAsync(plan.Inputs.Head, plan.Policy.Commit, clean: false, cancellationToken) is string notMoved
                ? (CheckVerdict.Error, notMoved, null, null)
                : await GatesJudge.RunAsync(worktree, plan, context, plan.Inputs.EvidenceDirectory, budget, cancellationToken);
            // Only when the base had none either: a pull request that turns the test projects into ones red/green cannot run must
            // not lose the check that nothing was deleted.
            bool baseHadNone = baseListing is null || baseListing.Outcome == TreeOutcome.NoTestApps;
            if (head.Listing.Outcome == TreeOutcome.NoTestApps && baseHadNone && gates.TestStep is string testStep)
            {
                // Without a test project red/green can run, the gates' own test step judges the suite at head.
                greenAtHead = greenAtHead with
                {
                    Verdict = testStep switch { "PASS" => CheckVerdict.Pass, "FAIL" => CheckVerdict.Fail, "ERROR" => CheckVerdict.Error, _ => CheckVerdict.NotEarned },
                    Why = $"no test project red/green can run (xUnit or NUnit); the gates' test step judged the suite: {testStep}",
                };
            }

            return new JudgeFindings(greenAtHead, cycles, trees.Builds)
            {
                Trace = trace,
                Gates = (gates.Verdict, gates.Why, gates.Result),
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
            await worktree.RemoveAsync();
        }
    }

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

    private static async Task<string> ParentAsync(JudgeInputs inputs, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        if (environment.Get(EnvironmentVariables.Temp) is { Length: > 0 } temp)
        {
            return Path.Combine(temp, LocalFolder);
        }

        if (inputs.Platform == JudgePlatform.Local)
        {
            ProcessResult common = await ProcessRunner.RunAsync("git", ["rev-parse", "--git-common-dir"], inputs.Root, cancellationToken);
            if (common.ExitCode == 0 && common.Output.Trim() is { Length: > 0 } gitDirectory)
            {
                return Path.Combine(Path.GetFullPath(gitDirectory, inputs.Root), LocalFolder);
            }
        }

        return Path.Combine(Path.GetTempPath(), LocalFolder);
    }
}
