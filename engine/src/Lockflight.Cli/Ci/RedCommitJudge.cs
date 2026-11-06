using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Re-runs every red commit (steps P2 and P3). For each one in topological order: its fork point's inventory, its own tree built and
/// listed, then judged by <see cref="RedCandidate"/>, the code <c>lockflight red</c> checks a red with before it commits it. The
/// trailer says only where to look. Tests that name only structural requirements are not part of a cycle, as in <c>redgreen
/// red</c>; trace checks them (<see cref="StructuralJudge"/>).
/// </summary>
internal sealed class RedCommitJudge(TreeBuilder trees, TimeProvider time)
{
    /// <param name="structuralIds">The head spec's structural requirements: a test naming only those is not part of a cycle.</param>
    public async Task<IReadOnlyList<CycleJudgement>> JudgeAsync(JudgePlan plan, IReadOnlySet<string> structuralIds, CancellationToken cancellationToken)
    {
        List<CycleJudgement> cycles = [];
        RedCycleHistory history = new(time);
        RedCandidate candidate = new(trees, time);
        foreach (RedCommit red in plan.Reds.Cycles)
        {
            string forkPoint = red.ForkPoint ?? plan.MergeBase!;
            TreeListing fork = await trees.ListAsync(forkPoint, forkPoint == plan.MergeBase ? "base" : "fork point", fresh: false, cancellationToken);
            if (fork.AsBaseline is not IReadOnlyList<TestCase> baseInventory)
            {
                cycles.Add(RedCandidate.NotJudged(red, fork, $"the fork point {CommitIds.Short(forkPoint)} could not be listed ({TreeOutcomeNames.ToName(fork.Outcome)}: {fork.Detail})"));
                continue;
            }

            TreeListing tree = await trees.ListAsync(red.Commit, $"red {red.Label}", fresh: true, cancellationToken);
            CycleJudgement cycle = await candidate.JudgeAsync(red, tree, ForkInventory.From(baseInventory), history, structuralIds, cancellationToken);
            cycles.Add(cycle);
            history.Add(cycle);
        }

        return cycles;
    }
}
