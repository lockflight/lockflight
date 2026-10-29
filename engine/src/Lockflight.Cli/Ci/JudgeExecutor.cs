using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Runs the steps of a plan that build: opens the judge's worktree (in <c>LOCKFLIGHT_TEMP</c> when the template sets it, else
/// locally in the repository's git directory, else the system's temporary folder), runs the head's suite (P1), lists the base (P2)
/// and re-runs the red commits (P3), then judges green over what they found, and removes the worktree again whatever happened.
/// </summary>
internal static class JudgeExecutor
{
    private const string LocalFolder = "lockflight";

    public static async Task<JudgeFindings> RunAsync(JudgePlan plan, EnvironmentReader environment, TimeProvider time, CancellationToken cancellationToken)
    {
        bool judgesReds = plan.Levels[CheckId.Red] != CheckLevel.Off && !plan.Reds.OverCap && plan.Reds.Cycles.Count > 0 && plan.MergeBase is not null;
        if (plan.Levels[CheckId.Green] == CheckLevel.Off)
        {
            return JudgeFindings.None;
        }

        string root = plan.Inputs.Root;
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
            TreeBuilder trees = new(worktree, plan.Policy, root, time);
            HeadRun head = await HeadRunner.RunAsync(trees, plan, cancellationToken);
            TreeListing? baseListing = plan.MergeBase is string mergeBase ? await trees.ListAsync(mergeBase, "base", fresh: false, cancellationToken) : null;
            IReadOnlyList<CycleJudgement>? cycles = judgesReds ? await new RedCommitJudge(trees, time).JudgeAsync(plan, cancellationToken) : null;
            GreenJudgement greenAtHead = baseListing is { AsBaseline: null }
                ? new GreenJudgement(CheckVerdict.NotEarned, $"the base {CommitIds.Short(plan.MergeBase!)} could not be listed to compare with ({TreeOutcomeNames.ToName(baseListing.Outcome)}: {baseListing.Detail})", 0, 0, [], [], new Dictionary<string, string>(), [])
                : GreenJudge.Decide(head, baseListing?.AsBaseline, cycles ?? []);
            return new JudgeFindings(greenAtHead, cycles, trees.Builds);
        }
        finally
        {
            await worktree.RemoveAsync();
        }
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
