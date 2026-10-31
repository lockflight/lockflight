using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Runs the head's version of some tests on older code: the older commit is built with the head's whole test side laid over it
/// (files the head no longer has removed, the base policy laid over again), each test's fingerprint there must be the head's, and
/// the tests are classified as a red's are. The D4 idea: a changed test must still tell the old code from the new. Paths in
/// <c>keep</c> (what the target branch alone changed since the older commit) keep the older commit's version.
/// </summary>
internal sealed class OverlayRecheck(TreeBuilder trees, TimeProvider time)
{
    public async Task<IReadOnlyList<RecheckResult>> RunAsync(
        JudgePlan plan, string olderCommit, IReadOnlyList<string> keys, TestSide side, TreeListing head, IReadOnlySet<string> keep, string purpose, CancellationToken cancellationToken)
    {
        string root = plan.Inputs.Root;
        IReadOnlyList<string> overlay = [.. side.Of(await JudgeGit.TreeFilesAsync(root, plan.Inputs.Head, cancellationToken) ?? []).Where(path => !keep.Contains(path))];
        HashSet<string> overlaid = [.. overlay];
        string[] deleted = [.. (await JudgeGit.TreeFilesAsync(root, olderCommit, cancellationToken) ?? []).Where(side.IsTestSide).Where(path => !overlaid.Contains(path) && !keep.Contains(path))];
        TreeListing tree = await trees.ListOverlaidAsync(olderCommit, plan.Inputs.Head, overlay, deleted, purpose, cancellationToken);
        if (tree.Inventory is not IReadOnlyList<TestCase> inventory)
        {
            string why = $"the head's test files do not build on {CommitIds.Short(olderCommit)}'s code ({TreeOutcomeNames.ToName(tree.Outcome)})";
            return [.. keys.Select(key => new RecheckResult(key, RecheckOutcome.Uncheckable, null, why))];
        }

        TestCase[] runnable = [.. inventory.Where(test => keys.Contains(test.Key, StringComparer.Ordinal)
            && tree.Fingerprints.GetValueOrDefault(test.Key) is string print && print == head.Fingerprints.GetValueOrDefault(test.Key))];
        List<RecheckResult> results = [.. keys.Where(key => runnable.All(test => test.Key != key)).Select(key =>
            new RecheckResult(key, RecheckOutcome.Uncheckable, null, $"on {CommitIds.Short(olderCommit)}'s code the test is not the head's (its fingerprint differs)"))];
        if (runnable.Length == 0)
        {
            return results;
        }

        DateTimeOffset deadline = time.GetUtcNow().Add(trees.ProcessLimit);
        if (await tree.Workspace.RunAsync(runnable, cancellationToken) is not IReadOnlyList<ExecutedTest> run)
        {
            results.AddRange(runnable.Select(test => new RecheckResult(test.Key, RecheckOutcome.Uncheckable, null, $"the test runner produced no report for {tree.Workspace.FailedApp}")));
            return results;
        }

        RedEvaluationResult evaluation = await RedEvaluation.ClassifyAsync(tree.Workspace, trees.Root, inventory, runnable, run, deadline, time, cancellationToken);
        results.AddRange(evaluation.Classified.Select(test => new RecheckResult(
            test.Key,
            RedClassNames.IsAccepted(test.RedClass) ? RecheckOutcome.Red : RecheckOutcome.NotRed,
            test.RedClass,
            evaluation.Verdicts.TryGetValue(test.Key, out RedVerdict? verdict) ? verdict.Why : RedClassNames.ToName(test.RedClass))));
        return results;
    }
}
