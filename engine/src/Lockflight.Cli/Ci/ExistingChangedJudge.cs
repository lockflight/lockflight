using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Existing tests changed (advisory): a base test still at head whose fingerprint changed, or whose project's test inputs (or a
/// shared test-helper project, or a runner setting) changed since the base, is run in its head version on the base's code with the
/// head's whole test side. Failing there in production code means it still tells the old behaviour from the new, as a legitimate
/// change of expectation does; passing there means it no longer does, which is the cheapest way to fake a green. A refactor of a
/// test that keeps its expectation also passes there, so a person decides; the check is advisory until the closure fingerprint
/// makes its false alarms rare.
/// </summary>
internal sealed class ExistingChangedJudge(TreeBuilder trees, TimeProvider time)
{
    public async Task<IReadOnlyList<ExistingChangedTest>> JudgeAsync(JudgePlan plan, TreeListing head, TreeListing baseListing, CancellationToken cancellationToken)
    {
        if (head.Inventory is not IReadOnlyList<TestCase> headInventory || baseListing.Inventory is not IReadOnlyList<TestCase> baseInventory || plan.MergeBase is not string mergeBase)
        {
            return [];
        }

        string root = plan.Inputs.Root;
        TestSide side = await TestSide.ReadAsync(root, plan.Inputs.Head, head, trees.Root, plan.Policy.Ci, cancellationToken);
        HashSet<string> atBase = [.. await JudgeGit.TreeFilesAsync(root, mergeBase, cancellationToken) ?? []];
        string[] changedInputs = [.. (await JudgeGit.ChangedFilesAsync(root, mergeBase, plan.Inputs.Head, cancellationToken) ?? []).Where(atBase.Contains).Where(side.IsTestSide)];
        IReadOnlySet<string> helpers = await side.HelperSourcesAsync(root, mergeBase, changedInputs, cancellationToken);
        HashSet<string> atHead = [.. headInventory.Select(test => test.Key)];
        string[] changed = [.. baseInventory.Select(test => test.Key).Where(atHead.Contains).Where(key =>
            baseListing.Fingerprints.GetValueOrDefault(key) is not string before
            || head.Fingerprints.GetValueOrDefault(key) != before
            || changedInputs.Any(path => side.Affects(path, key[..key.IndexOf(':', StringComparison.Ordinal)], helpers)))];
        if (changed.Length == 0)
        {
            return [];
        }

        IReadOnlyList<RecheckResult> rechecked = await new OverlayRecheck(trees, time).RunAsync(plan, mergeBase, changed, side, head, new HashSet<string>(StringComparer.Ordinal), "existing tests on base", cancellationToken);
        return [.. rechecked.Select(result => result.Outcome switch
        {
            RecheckOutcome.Red => new ExistingChangedTest(result.Key, ExistingChange.Rechecked, $"changed, and fails on the base's code: {result.Why}"),
            RecheckOutcome.NotRed => new ExistingChangedTest(result.Key, ExistingChange.PassesOnBase, "changed, and its head version passes on the base's code: it no longer tells the old behaviour from the new"),
            _ => new ExistingChangedTest(result.Key, ExistingChange.Uncheckable, result.Why),
        })];
    }
}
