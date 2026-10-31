using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Tests since red (step P4): each accepted cycle's tests compared from the red commit to head. A test whose fingerprint is the
/// same, with no test input, shared test-helper project, helper or hook source, project file or runner setting it depends on
/// changed, deleted or added since red (<see cref="TestSide"/>), is unchanged: the red at that commit is the proof. Any other
/// test was amended. Its head version must fail again on the red commit's code: the red commit is built with the head's whole
/// test side laid over it (not only the test's own file, so a helper gutted elsewhere is part of the re-check), the test's
/// fingerprint there must be the head's, and it is classified as a red is. And its red-commit version must pass on the head's
/// code, so the head's code still meets the bar the test went red for: otherwise the expectation changed, which is NOT-EARNED. An
/// amend whose head version has fewer rows, or fewer rows times assertion calls, than the red commit's version fails
/// (<see cref="SinceRed.AmendWeakened"/>). A weakened amend with as many rows and assertions is accepted when the head's code
/// still meets the red commit's version: the bar held, though the test now guards less.
/// Test files added for later cycles are not compared, unless they hold an assembly-wide hook: adding tests is what later cycles do.
/// A file the pull request never changed, the same at head as on the target branch and the same at the red as where the red
/// forked from it, came from the target ("Update branch", a merge of the target): it is no amend, and the re-check keeps the red
/// commit's version of it.
/// </summary>
internal sealed class SinceRedJudge(TreeBuilder trees, TimeProvider time)
{
    private const int MaxFilesListed = 20;

    public async Task<IReadOnlyList<SinceRedTest>> JudgeAsync(
        JudgePlan plan, TreeListing head, IReadOnlyList<ExecutedTest>? headResults, IReadOnlyList<CycleJudgement> cycles, CancellationToken cancellationToken)
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
            IReadOnlySet<string> fromTarget = await FromTargetAsync(plan, cycle.Red, cancellationToken);
            string[] changedInputs = [.. (await JudgeGit.ChangedFilesAsync(root, red, plan.Inputs.Head, cancellationToken) ?? [])
                .Where(side.IsTestSide).Where(path => !fromTarget.Contains(path))];
            IReadOnlySet<string> helpers = await side.HelperSourcesAsync(root, red, plan.Inputs.Head, changedInputs, cancellationToken);
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
                results.AddRange(await RecheckAsync(plan, cycle, amended, side, head, headResults, changedInputs, fromTarget, cancellationToken));
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
        JudgePlan plan, CycleJudgement cycle, List<RedGreenTest> amended, TestSide side, TreeListing head, IReadOnlyList<ExecutedTest>? headResults, string[] changedInputs,
        IReadOnlySet<string> fromTarget, CancellationToken cancellationToken)
    {
        string label = cycle.Red.Label!;
        string[] changedFiles = [.. (await JudgeGit.ChangedFilesAsync(plan.Inputs.Root, cycle.Red.Commit, plan.Inputs.Head, cancellationToken) ?? [])
            .Where(side.IsTestSide).Where(path => !fromTarget.Contains(path)).Take(MaxFilesListed)];
        IReadOnlyList<RecheckResult> rechecked = await new OverlayRecheck(trees, time).RunAsync(
            plan, cycle.Red.Commit, [.. amended.Select(test => test.Key)], side, head, fromTarget, $"re-check {label}", cancellationToken);
        string changed = changedInputs.Length > 0 ? string.Join(", ", changedInputs.Take(3)) : "its code";
        string[] stillRed = [.. rechecked.Where(result => result.Outcome == RecheckOutcome.Red).Select(result => result.Key)];
        Dictionary<string, OriginalCheck> originals = stillRed.Length == 0
            ? []
            : (await new OverlayRecheck(trees, time).RunOriginalAsync(plan, cycle, stillRed, side, $"original {label}", cancellationToken)).ToDictionary(check => check.Key, StringComparer.Ordinal);
        // Rows are compared on the same code: the head's version as the head run ran it, the red commit's version on the head's code.
        return rechecked.Select(result => Decide(result, originals.GetValueOrDefault(result.Key), headResults?.Count(row => row.Key == result.Key) ?? 0, label, changed, changedFiles));
    }

    /// <summary>
    /// One amended test's state from its two re-checks: weakened (fewer rows or assertions than the red commit's version) fails
    /// whatever else held; accepted needs the head's version red on the red commit's code and the red commit's version passing on
    /// the head's code; a test that is red there but whose original does not pass (or could not be run) changed its expectation.
    /// </summary>
    private static SinceRedTest Decide(RecheckResult result, OriginalCheck? original, int headRows, string label, string changed, string[] changedFiles)
    {
        if (original is not null && Dropped(headRows, result.Assertions, original) is string dropped)
        {
            return new SinceRedTest(result.Key, label, SinceRed.AmendWeakened,
                $"changed since its red ({changed}), and it has {dropped}: the amend dropped part of what the test checked; restore it, or prove the new test with a new red commit for this cycle (Lockflight-Red: {label})",
                changedFiles, result.Class);
        }

        return result.Outcome switch
        {
            RecheckOutcome.Red when original is { Passed: true } => new SinceRedTest(result.Key, label, SinceRed.AmendAccepted,
                $"changed since its red ({changed}); the head's version still fails on the red commit's code ({RedClassNames.ToName(result.Class!.Value)}) and the red commit's version passes on the head's code", changedFiles, result.Class),
            RecheckOutcome.Red => new SinceRedTest(result.Key, label, SinceRed.AmendChangedExpectation,
                $"changed since its red ({changed}): the head's version still fails on the red commit's code ({RedClassNames.ToName(result.Class!.Value)}), but {original?.Why ?? "the red commit's version was not run"}, so the expectation changed after red; prove the new expectation with a new red commit for this cycle (Lockflight-Red: {label})", changedFiles, result.Class),
            RecheckOutcome.NotRed => new SinceRedTest(result.Key, label, SinceRed.AmendRejected,
                $"the test as it is at head is {RedClassNames.ToName(result.Class!.Value)} on the code as it was at red: it no longer checks the behaviour it went red for", changedFiles, result.Class),
            _ => new SinceRedTest(result.Key, label, SinceRed.AmendUncheckable,
                $"{result.Why}; amend a test before the next cycle starts, or keep each story's tests in their own file", changedFiles, null),
        };
    }

    /// <summary>
    /// What the head's version has fewer of than the red commit's ("2 rows where the red commit's version has 3"), both run on the
    /// head's code: rows, or rows times assertion calls (so a fact with three asserts may become a theory with three rows); null
    /// when nothing is known to be dropped.
    /// </summary>
    private static string? Dropped(int headRows, int? headAssertions, OriginalCheck original)
    {
        if (headRows > 0 && original.Rows > headRows)
        {
            return $"{headRows} rows where the red commit's version has {original.Rows}";
        }

        return headRows > 0 && original.Rows > 0 && headAssertions is int now && original.Assertions is int before && before * original.Rows > now * headRows
            ? $"{headRows} rows of {now} assertion calls where the red commit's version has {original.Rows} rows of {before}"
            : null;
    }
}
