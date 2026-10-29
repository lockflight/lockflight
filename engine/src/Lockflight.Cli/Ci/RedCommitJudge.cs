using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Re-runs every red commit (steps P2 and P3). For each one in topological order: its fork point's inventory, its own tree built
/// (a build failure is COMPILE_RED), the tests new there (not at the fork point, not new at an earlier red), no known test deleted,
/// skipped or made explicit, then the new tests run and classified by <see cref="RedEvaluation"/>, as <c>redgreen red</c> does.
/// A cycle is accepted only when every new test is RED_STUB or RED_ASSERT. The trailer says only where to look.
/// </summary>
internal sealed class RedCommitJudge(TreeBuilder trees, TimeProvider time)
{
    public async Task<IReadOnlyList<CycleJudgement>> JudgeAsync(JudgePlan plan, CancellationToken cancellationToken)
    {
        List<CycleJudgement> cycles = [];
        HashSet<string> earlierNew = new(StringComparer.Ordinal);
        List<RedGreenCycle> accepted = [];
        foreach (RedCommit red in plan.Reds.Cycles)
        {
            string forkPoint = red.ForkPoint ?? plan.MergeBase!;
            TreeListing fork = await trees.ListAsync(forkPoint, forkPoint == plan.MergeBase ? "base" : "fork point", cancellationToken);
            if (fork.Inventory is not IReadOnlyList<TestCase> baseInventory)
            {
                cycles.Add(NotJudged(red, fork, $"the fork point {CommitIds.Short(forkPoint)} could not be listed ({TreeOutcomeNames.ToName(fork.Outcome)}: {fork.Detail})"));
                continue;
            }

            CycleJudgement cycle = await JudgeCycleAsync(red, baseInventory, earlierNew, accepted, cancellationToken);
            cycles.Add(cycle);
            earlierNew.UnionWith(cycle.Tests.Select(test => test.Key));
            if (cycle.Accepted)
            {
                accepted.Add(new RedGreenCycle(accepted.Count + 1, time.GetUtcNow(), GreenAt: null, cycle.Tests) { Snapshot = red.Commit });
            }
        }

        return cycles;
    }

    private async Task<CycleJudgement> JudgeCycleAsync(
        RedCommit red, IReadOnlyList<TestCase> baseInventory, HashSet<string> earlierNew, List<RedGreenCycle> accepted, CancellationToken cancellationToken)
    {
        TreeListing tree = await trees.ListAsync(red.Commit, $"red {red.Label}", cancellationToken);
        if (tree.Outcome == TreeOutcome.CompileFailed)
        {
            return Judged(red, tree.Outcome, [], CheckVerdict.Fail, $"COMPILE_RED: the tests do not compile at the red commit; declare the members they use as stubs. {tree.Detail}");
        }

        if (tree.Inventory is not IReadOnlyList<TestCase> inventory)
        {
            return NotJudged(red, tree, tree.Detail ?? "the tree could not be listed");
        }

        RedGreenRecord record = new()
        {
            BaselineCommit = red.ForkPoint ?? string.Empty,
            BaselineAt = time.GetUtcNow(),
            BaselineTests = baseInventory.Select(test => test.Key).ToHashSet(StringComparer.Ordinal),
            BaselineSkipped = baseInventory.Where(test => test.SkipReason is not null).Select(test => test.Key).ToHashSet(StringComparer.Ordinal),
            BaselineExplicit = baseInventory.Where(test => test.IsExplicit).Select(test => test.Key).ToHashSet(StringComparer.Ordinal),
            TestProjects = [],
            Cycles = [.. accepted],
        };
        if (SuiteProblem(record, inventory) is string problem)
        {
            return Judged(red, tree.Outcome, [], CheckVerdict.Fail, problem);
        }

        TestCase[] fresh = [.. inventory.Where(test => !record.BaselineTests.Contains(test.Key) && !earlierNew.Contains(test.Key))];
        if (fresh.Length == 0)
        {
            return Judged(red, tree.Outcome, [], CheckVerdict.Fail, "the red commit adds no tests; add the story's tests to it, or drop its trailer");
        }

        DateTimeOffset deadline = time.GetUtcNow().Add(trees.ProcessLimit);
        if (await tree.Workspace.RunAsync(fresh, cancellationToken) is not IReadOnlyList<ExecutedTest> results)
        {
            return Judged(red, tree.Outcome, [], CheckVerdict.Error, $"the test runner produced no report for {tree.Workspace.FailedApp}");
        }

        RedEvaluationResult evaluation = await RedEvaluation.ClassifyAsync(tree.Workspace, trees.Root, inventory, fresh, results, deadline, time, cancellationToken);
        RedGreenTest[] rejected = [.. evaluation.Classified.Where(test => !RedClassNames.IsAccepted(test.RedClass))];
        string why = rejected.Length == 0
            ? $"{evaluation.Classified.Count} new tests, each RED_STUB or RED_ASSERT"
            : $"{rejected.Length} of {evaluation.Classified.Count} new tests do not fail for the right reason: "
              + string.Join("; ", rejected.Select(test => test.RedClass).Distinct().Select(redClass => $"{RedClassNames.ToName(redClass)} ({RedClassFixes.For(redClass)})"));
        return new CycleJudgement(red, tree.Outcome, evaluation.Classified, evaluation.Verdicts, rejected.Length == 0 ? CheckVerdict.Pass : CheckVerdict.Fail, why);
    }

    /// <summary>A known test (at the fork point or new at an earlier red) deleted, newly skipped or made explicit at this red.</summary>
    private static string? SuiteProblem(RedGreenRecord record, IReadOnlyList<TestCase> inventory)
    {
        HashSet<string> current = [.. inventory.Select(test => test.Key)];
        string[] deleted = [.. record.KnownTests.Where(key => !current.Contains(key)).Order(StringComparer.Ordinal).Take(RedGreenSession.MaxTestsListed)];
        if (deleted.Length > 0)
        {
            return $"tests disappeared since the fork point at this red: {string.Join(", ", deleted)}; a red adds tests, it does not delete them";
        }

        List<string> weakened = SuiteChecks.WeakenedKnownTests(record, inventory);
        return weakened.Count > 0 ? $"{string.Join("; ", weakened)}; a red adds tests, it does not switch existing ones off" : null;
    }

    private static CycleJudgement NotJudged(RedCommit red, TreeListing tree, string why) =>
        Judged(red, tree.Outcome, [], tree.Outcome == TreeOutcome.Broken ? CheckVerdict.Error : CheckVerdict.NotEarned, why);

    private static CycleJudgement Judged(RedCommit red, TreeOutcome build, IReadOnlyList<RedGreenTest> tests, CheckVerdict verdict, string why) =>
        new(red, build, tests, new Dictionary<string, RedVerdict>(StringComparer.Ordinal), verdict, why);
}
