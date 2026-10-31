using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Runs the head's version of some tests on older code: the older commit is built with the head's whole test side laid over it
/// (files the head no longer has removed, test files added since that hold only other tests left out, the base policy laid over
/// again), each test's fingerprint there must be the head's, and
/// the tests are classified as a red's are. The D4 idea: a changed test must still tell the old code from the new. Paths in
/// <c>keep</c> (what the target branch alone changed since the older commit) keep the older commit's version where it has one.
/// </summary>
internal sealed class OverlayRecheck(TreeBuilder trees, TimeProvider time)
{
    public async Task<IReadOnlyList<RecheckResult>> RunAsync(
        JudgePlan plan, string olderCommit, IReadOnlyList<string> keys, TestSide side, TreeListing head, IReadOnlySet<string> keep, string purpose, CancellationToken cancellationToken)
    {
        string root = plan.Inputs.Root;
        HashSet<string> older = [.. await JudgeGit.TreeFilesAsync(root, olderCommit, cancellationToken) ?? []];
        // Only a path the older commit has can keep its version there; one the target added comes from the head like the rest.
        HashSet<string> kept = [.. older.Where(keep.Contains)];
        IReadOnlyList<string> overlay = [.. (await side.OverlayAsync(root, plan.Inputs.Head, await JudgeGit.TreeFilesAsync(root, plan.Inputs.Head, cancellationToken) ?? [], older, cancellationToken))
            .Where(path => !kept.Contains(path))];
        HashSet<string> overlaid = [.. overlay];
        string[] deleted = [.. older.Where(side.IsTestSide).Where(path => !overlaid.Contains(path) && !kept.Contains(path))];
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
        IReadOnlyDictionary<string, int> assertions = TestFingerprints.Assertions(tree.Workspace, runnable);
        results.AddRange(evaluation.Classified.Select(test => new RecheckResult(
            test.Key,
            RedClassNames.IsAccepted(test.RedClass) ? RecheckOutcome.Red : RecheckOutcome.NotRed,
            test.RedClass,
            evaluation.Verdicts.TryGetValue(test.Key, out RedVerdict? verdict) ? verdict.Why : RedClassNames.ToName(test.RedClass))
        {
            Assertions = assertions.TryGetValue(test.Key, out int count) ? count : null,
        }));
        return results;
    }

    /// <summary>
    /// Runs the red commit's version of some amended tests on the head's code: the head is built with the red commit's whole test
    /// side laid over it (the head's C# test-side files the red commit lacks removed), each test's fingerprint there must be the red
    /// commit's, and it must pass every row. The other half of the D4 idea: the amend kept the bar the implementation had to meet.
    /// Its rows and assertions are counted, so an amend that dropped some is told apart.
    /// </summary>
    public async Task<IReadOnlyList<OriginalCheck>> RunOriginalAsync(
        JudgePlan plan, CycleJudgement cycle, IReadOnlyList<string> keys, TestSide side, string purpose, CancellationToken cancellationToken)
    {
        string root = plan.Inputs.Root;
        string red = cycle.Red.Commit;
        IReadOnlyList<string> overlay = side.Of(await JudgeGit.TreeFilesAsync(root, red, cancellationToken) ?? []);
        HashSet<string> overlaid = [.. overlay];
        // C# files added since red are left out (later cycles' tests, helpers and hooks); project files and other files added since
        // stay, so a test project a later cycle added still builds.
        string[] deleted = [.. side.Of(await JudgeGit.TreeFilesAsync(root, plan.Inputs.Head, cancellationToken) ?? [])
            .Where(path => !overlaid.Contains(path) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))];
        TreeListing tree = await trees.ListOverlaidAsync(plan.Inputs.Head, red, overlay, deleted, purpose, cancellationToken);
        if (tree.Inventory is not IReadOnlyList<TestCase> inventory)
        {
            string why = $"the red commit's test files do not build on the head's code ({TreeOutcomeNames.ToName(tree.Outcome)}; after a change to an API the red tests use, prove the amend with a new red commit)";
            return [.. keys.Select(key => new OriginalCheck(key, null, why))];
        }

        TestCase[] runnable = [.. inventory.Where(test => keys.Contains(test.Key, StringComparer.Ordinal)
            && tree.Fingerprints.GetValueOrDefault(test.Key) is string print && print == cycle.FingerprintsAtRed.GetValueOrDefault(test.Key))];
        List<OriginalCheck> results = [.. keys.Where(key => runnable.All(test => test.Key != key)).Select(key =>
            new OriginalCheck(key, null, "on the head's code the red commit's version of the test is not the one that went red (its fingerprint differs)"))];
        if (runnable.Length == 0)
        {
            return results;
        }

        if (await tree.Workspace.RunAsync(runnable, cancellationToken) is not IReadOnlyList<ExecutedTest> run)
        {
            results.AddRange(runnable.Select(test => new OriginalCheck(test.Key, null, $"the test runner produced no report for {tree.Workspace.FailedApp}")));
            return results;
        }

        IReadOnlyDictionary<string, int> assertions = TestFingerprints.Assertions(tree.Workspace, runnable);
        foreach (TestCase test in runnable)
        {
            ExecutedTest[] rows = [.. run.Where(row => row.Key == test.Key)];
            OriginalCheck check = rows.Length == 0
                ? new OriginalCheck(test.Key, null, "the red commit's version did not run on the head's code")
                : rows.All(row => row.Outcome == TestOutcome.Pass)
                ? new OriginalCheck(test.Key, true, "the red commit's version passes on the head's code")
                : new OriginalCheck(test.Key, false, $"the red commit's version fails on the head's code ({rows.First(row => row.Outcome != TestOutcome.Pass).ExceptionType ?? "not passed"})");
            results.Add(check with { Rows = rows.Length, Assertions = assertions.TryGetValue(test.Key, out int count) ? count : null });
        }

        return results;
    }
}
