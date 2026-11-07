using System.Globalization;
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
        return await RunOverlaidAsync(plan, olderCommit, keys, overlay, deleted, head, purpose, cancellationToken);
    }

    /// <summary>
    /// As <see cref="RunAsync"/>, with the head's files to lay over the older commit and the paths to delete there given: the
    /// structural check lays over only the files that declare its tests and the test projects' project files.
    /// </summary>
    public async Task<IReadOnlyList<RecheckResult>> RunOverlaidAsync(
        JudgePlan plan, string olderCommit, IReadOnlyList<string> keys, IReadOnlyList<string> overlay, IReadOnlyList<string> deleted, TreeListing head, string purpose, CancellationToken cancellationToken)
    {
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
            PassedEveryRow = SuiteChecks.PassedEveryRow(run, test.Key),
        }));
        return results;
    }

    /// <summary>
    /// Runs the red commit's version of some amended tests on the head's code: the head is built with the red commit's whole test
    /// side laid over it (the head's C# test-side files the red commit lacks removed), each test's fingerprint there must be the red
    /// commit's, and it must pass every row. The other half of the D4 idea: the amend kept the bar the implementation had to meet.
    /// Its rows and assertions are counted, so an amend that dropped some is told apart.
    /// </summary>
    public Task<IReadOnlyList<OriginalCheck>> RunOriginalAsync(
        JudgePlan plan, CycleJudgement cycle, IReadOnlyList<string> keys, TestSide side, string purpose, CancellationToken cancellationToken) =>
        RunOlderVersionsAsync(plan, cycle.Red.Commit, cycle.FingerprintsAtRed, keys, side, new OlderVersion(
            "the red commit's version",
            "the red commit's test files do not build on the head's code ({0}; after a change to an API the red tests use, prove the amend with a new red commit)",
            "on the head's code the red commit's version of the test is not the one that went red (its fingerprint differs)"), purpose, cancellationToken);

    /// <summary>
    /// In a structural-only change, runs the base's version of the existing tests the pull request changed on the head's code: the
    /// head is built with the base's whole test side laid over it (helpers and test inputs included, the head's C# test-side files
    /// the base lacks removed), each test's fingerprint there must be the base's, and it must pass every row. A refactor keeps what
    /// the old test pinned; a test or shared helper weakened in the pull request does not help, because the base's version runs.
    /// </summary>
    public Task<IReadOnlyList<OriginalCheck>> RunBaseVersionsAsync(
        JudgePlan plan, string mergeBase, IReadOnlyDictionary<string, string> baseFingerprints, IReadOnlyList<string> keys, TestSide side, CancellationToken cancellationToken) =>
        RunOlderVersionsAsync(plan, mergeBase, baseFingerprints, keys, side, new OlderVersion(
            "the base's version",
            "the base's test files do not build on the head's code ({0}); keep the API the old tests use for this pull request",
            "on the head's code the base's version of the test is not the base's (its fingerprint differs)"), "existing tests' old versions on head", cancellationToken);

    private async Task<IReadOnlyList<OriginalCheck>> RunOlderVersionsAsync(
        JudgePlan plan, string olderCommit, IReadOnlyDictionary<string, string> olderFingerprints, IReadOnlyList<string> keys, TestSide side, OlderVersion version, string purpose, CancellationToken cancellationToken)
    {
        string root = plan.Inputs.Root;
        IReadOnlyList<string> overlay = side.Of(await JudgeGit.TreeFilesAsync(root, olderCommit, cancellationToken) ?? []);
        HashSet<string> overlaid = [.. overlay];
        // C# files added since the older commit are left out (later cycles' tests, new tests, helpers and hooks); project files and
        // other files added since stay, so a test project added later still builds.
        string[] deleted = [.. side.Of(await JudgeGit.TreeFilesAsync(root, plan.Inputs.Head, cancellationToken) ?? [])
            .Where(path => !overlaid.Contains(path) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))];
        TreeListing tree = await trees.ListOverlaidAsync(plan.Inputs.Head, olderCommit, overlay, deleted, purpose, cancellationToken);
        if (tree.Inventory is not IReadOnlyList<TestCase> inventory)
        {
            string why = string.Format(CultureInfo.InvariantCulture, version.NotBuilt, TreeOutcomeNames.ToName(tree.Outcome));
            return [.. keys.Select(key => new OriginalCheck(key, null, why))];
        }

        TestCase[] runnable = [.. inventory.Where(test => keys.Contains(test.Key, StringComparer.Ordinal)
            && tree.Fingerprints.GetValueOrDefault(test.Key) is string print && print == olderFingerprints.GetValueOrDefault(test.Key))];
        List<OriginalCheck> results = [.. keys.Where(key => runnable.All(test => test.Key != key)).Select(key => new OriginalCheck(key, null, version.NotSame))];
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
                ? new OriginalCheck(test.Key, null, $"{version.Name} did not run on the head's code")
                : rows.All(row => row.Outcome == TestOutcome.Pass)
                ? new OriginalCheck(test.Key, true, $"{version.Name} passes on the head's code")
                : new OriginalCheck(test.Key, false, $"{version.Name} fails on the head's code ({rows.First(row => row.Outcome != TestOutcome.Pass).ExceptionType ?? "not passed"})");
            results.Add(check with { Rows = rows.Length, Assertions = assertions.TryGetValue(test.Key, out int count) ? count : null });
        }

        return results;
    }

    /// <summary>How the messages name the older version: the red commit's or the base's.</summary>
    private sealed record OlderVersion(string Name, string NotBuilt, string NotSame);
}
