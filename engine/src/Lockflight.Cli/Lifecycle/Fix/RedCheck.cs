using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.Fix;

/// <summary>
/// Judges a candidate red the way the CI judge will: in one worktree per repository, kept between runs (<c>lf-red</c> under
/// <c>LOCKFLIGHT_TEMP</c>, else under the git common directory) so later builds are incremental, with the base policy laid over
/// every tree (<see cref="TreeBuilder"/>). The earlier reds on the branch are judged first, from the cache when this command
/// judged them before and in the worktree otherwise, so their tests are excluded from the candidate's new tests and the accepted
/// ones count as known. Then the candidate's fork point is listed and the candidate is judged by <see cref="RedCandidate"/>, the
/// judge's own code. The person's checkout, index and branch are never touched; the worktree is locked for the run.
/// </summary>
internal sealed class RedCheck(CliContext context, JudgePlan plan, RedCache? cache, bool dryRun)
{
    private const string WorktreeName = "lf-red";
    private const string LockSuffix = ".lock";
    private const int HashLength = 12;

    private static readonly TimeSpan GitTimeLimit = TimeSpan.FromMinutes(5);

    private TextWriter Output => context.Output;

    /// <summary>Where the kept worktree lives for the repository at <paramref name="root"/>; null when git cannot name its directory.</summary>
    public static async Task<string?> WorktreeDirectoryAsync(string root, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        ProcessResult common = await ProcessRunner.RunAsync("git", ["rev-parse", "--git-common-dir"], root, cancellationToken);
        if (common.ExitCode != 0 || common.Output.Trim() is not { Length: > 0 } gitDirectory)
        {
            return null;
        }

        string commonDirectory = Path.GetFullPath(gitDirectory, root);
        if (environment.Get(EnvironmentVariables.Temp) is { Length: > 0 } temp)
        {
            // One temporary folder can serve several repositories: the name says whose worktree it is.
            string owner = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(commonDirectory)))[..HashLength];
            return Path.Combine(temp, $"{WorktreeName}-{owner}");
        }

        return Path.Combine(commonDirectory, WorktreeName);
    }

    public async Task<RedCheckResult> RunAsync(RedCommit candidate, IReadOnlyList<RedCommit> earlier, IReadOnlySet<string> structuralIds, CancellationToken cancellationToken)
    {
        string root = plan.Inputs.Root;
        if (await WorktreeDirectoryAsync(root, context.Environment, cancellationToken) is not string directory)
        {
            return RedCheckResult.Failed("git could not name the repository's git directory");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        FileStream? held = Lock(directory + LockSuffix);
        if (held is null)
        {
            return RedCheckResult.Failed($"another lockflight red is using its worktree {directory}; wait for it to finish, then run again");
        }

        await using FileStream lockFile = held;
        if (await JudgeWorktree.OpenKeptAsync(root, directory, candidate.Commit, GitTimeLimit, cancellationToken) is not JudgeWorktree worktree)
        {
            return RedCheckResult.Failed($"git could not add a worktree at {directory}");
        }

        try
        {
            TreeBuilder trees = new(worktree, plan.Policy, root, new JudgeBudget(TimeSpan.FromMinutes(plan.Policy.Ci.TimeoutMinutes), context.Time), context.Time);
            RedCandidate judge = new(trees, context.Time);
            RedCycleHistory history = new(context.Time);
            foreach (RedCommit red in earlier)
            {
                await JudgeEarlierAsync(red, trees, judge, history, structuralIds, cancellationToken);
            }

            (ForkInventory? fork, TreeListing? forkListing) = await ForkAsync(plan.MergeBase!, trees, cancellationToken);
            if (fork is null)
            {
                return RedCheckResult.Judged(RedCandidate.NotJudged(candidate, forkListing!,
                    $"the fork point {CommitIds.Short(plan.MergeBase!)} could not be listed ({TreeOutcomeNames.ToName(forkListing!.Outcome)}: {forkListing.Detail})"), null);
            }

            TreeListing tree = await trees.ListAsync(candidate.Commit, $"red {candidate.Label}", fresh: true, cancellationToken);
            return RedCheckResult.Judged(await judge.JudgeAsync(candidate, tree, fork, history, structuralIds, cancellationToken), tree);
        }
        finally
        {
            await BuildServers.ShutdownAsync(worktree.Root, CancellationToken.None);
        }
    }

    /// <summary>
    /// The reds on the branch before this one, as the judge sees them: from the cache when this command judged them, else re-judged
    /// in the worktree (a hand-typed trailer, another machine, a deleted cache), one build each.
    /// </summary>
    private async Task JudgeEarlierAsync(RedCommit red, TreeBuilder trees, RedCandidate judge, RedCycleHistory history, IReadOnlySet<string> structuralIds, CancellationToken cancellationToken)
    {
        if (cache?.ReadRed(red.Commit) is CachedRed known)
        {
            history.Add(red.Commit, known.Accepted, known.Tests);
            return;
        }

        await Output.WriteLineAsync($"Judging the earlier red {red.Label} ({CommitIds.Short(red.Commit)}) first, as CI will: it is not in the cache, so it is built once.");
        (ForkInventory? fork, _) = await ForkAsync(red.ForkPoint ?? plan.MergeBase!, trees, cancellationToken);
        if (fork is null)
        {
            // As in the judge: a cycle whose fork point cannot be listed contributes no tests.
            return;
        }

        TreeListing tree = await trees.ListAsync(red.Commit, $"red {red.Label}", fresh: true, cancellationToken);
        CycleJudgement cycle = await judge.JudgeAsync(red, tree, fork, history, structuralIds, cancellationToken);
        history.Add(cycle);
        await Output.WriteLineAsync($"  earlier red {red.Label}: {(cycle.Accepted ? "accepted" : $"not accepted ({cycle.Why})")}");
        if (!dryRun && cycle.Verdict is CheckVerdict.Pass or CheckVerdict.Fail)
        {
            cache?.WriteRed(red.Commit, new CachedRed(cycle.Accepted, cycle.Tests));
        }
    }

    private async Task<(ForkInventory? Inventory, TreeListing? Listing)> ForkAsync(string commit, TreeBuilder trees, CancellationToken cancellationToken)
    {
        if (cache?.ReadFork(commit) is ForkInventory known)
        {
            return (known, null);
        }

        TreeListing listing = await trees.ListAsync(commit, "fork point", fresh: false, cancellationToken);
        if (listing.AsBaseline is not IReadOnlyList<TestCase> inventory)
        {
            return (null, listing);
        }

        ForkInventory fork = ForkInventory.From(inventory);
        if (!dryRun)
        {
            cache?.WriteFork(commit, fork);
        }

        return (fork, listing);
    }

    private static FileStream? Lock(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
