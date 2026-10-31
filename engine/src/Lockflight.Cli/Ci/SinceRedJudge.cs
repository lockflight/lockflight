using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Tests since red (step P4): each accepted cycle's tests compared from the red commit to head. A test whose fingerprint is the
/// same, with no test input, shared test-helper project or runner setting it depends on changed in a file that existed at red, is
/// unchanged: the red at that commit is the proof. Any other test was amended, and the head's version must fail again on the red
/// commit's code: the red commit is built with the head's whole test side laid over it (not only the test's own file, so a helper
/// gutted elsewhere is part of the re-check), the test's fingerprint there must be the head's, and it is classified as a red is.
/// A test files added for later cycles are not compared: adding tests is what later cycles do. A file the pull request never
/// changed, the same at head as on the target branch and the same at the red as where the red forked from it, came from the target
/// ("Update branch", a merge of the target): it is no amend, and the re-check keeps the red commit's version of it.
/// </summary>
internal sealed class SinceRedJudge(TreeBuilder trees, TimeProvider time)
{
    private const int MaxFilesListed = 20;

    public async Task<IReadOnlyList<SinceRedTest>> JudgeAsync(JudgePlan plan, TreeListing head, IReadOnlyList<CycleJudgement> cycles, CancellationToken cancellationToken)
    {
        string root = plan.Inputs.Root;
        if (head.Inventory is not IReadOnlyList<TestCase> headInventory)
        {
            return [];
        }

        TestSide side = await TestSide.ReadAsync(root, plan.Inputs.Head, head, trees.Root, plan.Policy.Ci, cancellationToken);
        List<SinceRedTest> results = [];
        foreach (CycleJudgement cycle in cycles.Where(cycle => cycle.Accepted))
        {
            string red = cycle.Red.Commit;
            HashSet<string> atRed = [.. await JudgeGit.TreeFilesAsync(root, red, cancellationToken) ?? []];
            IReadOnlySet<string> fromTarget = await FromTargetAsync(plan, cycle.Red, cancellationToken);
            string[] changedInputs = [.. (await JudgeGit.ChangedFilesAsync(root, red, plan.Inputs.Head, cancellationToken) ?? [])
                .Where(atRed.Contains).Where(side.IsTestSide).Where(path => !fromTarget.Contains(path))];
            IReadOnlySet<string> helpers = await side.HelperSourcesAsync(root, red, changedInputs, cancellationToken);
            List<RedGreenTest> amended = [];
            foreach (RedGreenTest test in cycle.Tests)
            {
                TestCase? atHead = headInventory.FirstOrDefault(candidate => candidate.Key == test.Key);
                string assembly = test.Key[..test.Key.IndexOf(':', StringComparison.Ordinal)];
                string[] inputs = [.. changedInputs.Where(path => side.Affects(path, assembly, helpers))];
                if (atHead is null)
                {
                    results.Add(new SinceRedTest(test.Key, cycle.Red.Label!, SinceRed.MissingAtHead, MissingWhy, [], null));
                }
                else if (cycle.FingerprintsAtRed.GetValueOrDefault(test.Key) is string before && head.Fingerprints.GetValueOrDefault(test.Key) == before && inputs.Length == 0)
                {
                    results.Add(new SinceRedTest(test.Key, cycle.Red.Label!, SinceRed.Unchanged, "unchanged since its red", [], null));
                }
                else
                {
                    amended.Add(test);
                }
            }

            if (amended.Count > 0)
            {
                results.AddRange(await RecheckAsync(plan, cycle, amended, side, head, changedInputs, fromTarget, cancellationToken));
            }
        }

        return results;
    }

    /// <summary>Why a cycle test missing at head is not earned, and the way out.</summary>
    public const string MissingWhy = "deleted or renamed after its red, so the red that proved it is gone; keep the name it went red with until the pull request merges (rename it after), or restore it";

    /// <summary>
    /// The paths the pull request never changed: the same at head as at the merge base, and the same at the red as at its fork
    /// point. Every difference between the red and head in them came from the target branch.
    /// </summary>
    private static async Task<IReadOnlySet<string>> FromTargetAsync(JudgePlan plan, RedCommit red, CancellationToken cancellationToken)
    {
        string root = plan.Inputs.Root;
        if (plan.MergeBase is not string mergeBase)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        string forkPoint = red.ForkPoint ?? mergeBase;
        if (await JudgeGit.ChangedFilesAsync(root, forkPoint, plan.Inputs.Head, cancellationToken) is not IReadOnlyList<string> sinceFork
            || await JudgeGit.ChangedFilesAsync(root, mergeBase, plan.Inputs.Head, cancellationToken) is not IReadOnlyList<string> byPullRequest
            || await JudgeGit.ChangedFilesAsync(root, forkPoint, red.Commit, cancellationToken) is not IReadOnlyList<string> byRed)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        HashSet<string> changed = [.. byPullRequest, .. byRed];
        return sinceFork.Where(path => !changed.Contains(path)).ToHashSet(StringComparer.Ordinal);
    }

    private async Task<IEnumerable<SinceRedTest>> RecheckAsync(
        JudgePlan plan, CycleJudgement cycle, List<RedGreenTest> amended, TestSide side, TreeListing head, string[] changedInputs, IReadOnlySet<string> fromTarget, CancellationToken cancellationToken)
    {
        string label = cycle.Red.Label!;
        string[] changedFiles = [.. (await JudgeGit.ChangedFilesAsync(plan.Inputs.Root, cycle.Red.Commit, plan.Inputs.Head, cancellationToken) ?? [])
            .Where(side.IsTestSide).Where(path => !fromTarget.Contains(path)).Take(MaxFilesListed)];
        IReadOnlyList<RecheckResult> rechecked = await new OverlayRecheck(trees, time).RunAsync(
            plan, cycle.Red.Commit, [.. amended.Select(test => test.Key)], side, head, fromTarget, $"re-check {label}", cancellationToken);
        string changed = changedInputs.Length > 0 ? string.Join(", ", changedInputs.Take(3)) : "its code";
        return rechecked.Select(result => result.Outcome switch
        {
            RecheckOutcome.Red => new SinceRedTest(result.Key, label, SinceRed.AmendAccepted,
                $"changed since its red ({changed}), and the head's version still fails on the red commit's code ({RedClassNames.ToName(result.Class!.Value)})", changedFiles, result.Class),
            RecheckOutcome.NotRed => new SinceRedTest(result.Key, label, SinceRed.AmendRejected,
                $"the test as it is at head is {RedClassNames.ToName(result.Class!.Value)} on the code as it was at red: it no longer checks the behaviour it went red for", changedFiles, result.Class),
            _ => new SinceRedTest(result.Key, label, SinceRed.AmendUncheckable,
                $"{result.Why}; amend a test before the next cycle starts, or keep each story's tests in their own file", changedFiles, null),
        });
    }
}
