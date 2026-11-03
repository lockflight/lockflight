using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The CI side of the structural check (<see cref="StructuralCheck"/>, shared with local trace): the structural tests' results from
/// the head run, then the fork point (merge base) built with the files that declare them and every non-C# file of the head's test
/// side laid over it, where they run and are classified as a red's tests are. A unit repository of a workspace shell is not checked: its
/// spec is the shell's.
/// </summary>
internal sealed class StructuralJudge(TreeBuilder trees, TimeProvider time)
{
    /// <summary>The structural requirement IDs of the head's spec for this pull request; none when there is none or it is the shell's.</summary>
    public static async Task<IReadOnlySet<string>> IdsAsync(JudgePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Shell is ShellPolicy)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        SpecResolution spec = await TreeSpecSource.ResolveAsync(plan, plan.Inputs.Branch, [], cancellationToken);
        return spec.SpecText is string text ? StructuralCheck.Ids(SpecDocument.Parse(text).Requirements) : new HashSet<string>(StringComparer.Ordinal);
    }

    /// <param name="baseInventory">The base's tests: a structural test the base already had proves nothing about this change and does not count.</param>
    public async Task<IReadOnlyList<StructuralTest>> JudgeAsync(
        JudgePlan plan, HeadRun head, IReadOnlyList<TestCase>? baseInventory, IReadOnlySet<string> structuralIds, CancellationToken cancellationToken)
    {
        if (structuralIds.Count == 0 || head.Listing.Inventory is not IReadOnlyList<TestCase> inventory)
        {
            return [];
        }

        HashSet<string> onBaseAlready = [.. (baseInventory ?? []).Select(test => test.Key)];
        TestCase[] tests = [.. inventory.Where(test => StructuralCheck.IsStructural(test.Requirements, structuralIds) && test.SkipReason is null && !test.IsExplicit
            && !onBaseAlready.Contains(test.Key))];
        if (tests.Length == 0)
        {
            return [];
        }

        TestCase[] passing = [.. tests.Where(test => head.Results is IReadOnlyList<ExecutedTest> results && SuiteChecks.PassedEveryRow(results, test.Key) && !head.PassedOnRetry.Contains(test.Key))];
        Dictionary<string, RecheckResult> onBase = plan.MergeBase is string mergeBase && passing.Length > 0
            ? (await OnBaseAsync(plan, mergeBase, head, passing, cancellationToken)).ToDictionary(result => result.Key, StringComparer.Ordinal)
            : new Dictionary<string, RecheckResult>(StringComparer.Ordinal);
        return [.. tests.Select(test => passing.Contains(test)
            ? onBase.TryGetValue(test.Key, out RecheckResult? result)
                ? Result(test, StructuralCheck.Classify(passesAtHead: true, result.Outcome == RecheckOutcome.Uncheckable ? null : result.Class, result.PassedEveryRow),
                    result.Outcome == RecheckOutcome.Uncheckable ? result.Why : $"{(result.Class is RedClass redClass ? RedClassNames.ToName(redClass) : "?")} on the base's code: {result.Why}")
                : Result(test, StructuralOutcome.Uncheckable, "no base to run it on")
            : Result(test, StructuralOutcome.FailsAtHead, "does not pass at head, or passed only when run again"))];
    }

    private static StructuralTest Result(TestCase test, StructuralOutcome outcome, string why) => new(test.Key, test.MethodFilter, test.Requirements, outcome, why);

    /// <summary>Runs the head's structural tests on the base's code with their own files and the test side's non-C# files laid over it.</summary>
    private async Task<IReadOnlyList<RecheckResult>> OnBaseAsync(JudgePlan plan, string mergeBase, HeadRun head, TestCase[] tests, CancellationToken cancellationToken)
    {
        string root = plan.Inputs.Root;
        TestSide side = await TestSide.ReadAsync(root, plan.Inputs.Head, head.Listing, trees.Root, plan.Policy.Ci, cancellationToken);
        List<string> overlay = [];
        foreach (string path in side.Of(await JudgeGit.TreeFilesAsync(root, plan.Inputs.Head, cancellationToken) ?? []))
        {
            // Every non-C# file of the test side (project files, snapshots, expected-value data, runner settings) comes from the head,
            // so a snapshot updated to new output cannot pass on the base against its old version.
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || (await JudgeGit.ShowFileAsync(root, plan.Inputs.Head, path, cancellationToken) is string text
                    && tests.Any(test => TestSourceFiles.Declares(text, test.Class, test.Method))))
            {
                overlay.Add(path);
            }
        }

        return await new OverlayRecheck(trees, time).RunOverlaidAsync(plan, mergeBase, [.. tests.Select(test => test.Key)], overlay, [], head.Listing, "structural tests on base", cancellationToken);
    }
}
