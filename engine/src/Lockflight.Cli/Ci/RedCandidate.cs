using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Reasons;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Judges one red commit whose tree was built and listed: a build failure is COMPILE_RED; no test known at the fork point or in an
/// accepted earlier cycle may be deleted, newly skipped or made explicit; the new tests (not at the fork point, not new at an earlier
/// red, not structural-only) are run and classified by <see cref="RedEvaluation"/>, and the red is accepted only when every one is
/// RED_STUB or RED_ASSERT. The CI judge and <c>lockflight red</c> both call it, so a red checked locally is judged by the same code
/// in CI.
/// </summary>
internal sealed class RedCandidate(TreeBuilder trees, TimeProvider time)
{
    /// <param name="structuralIds">The spec's structural requirements: a test naming only those is not part of a cycle.</param>
    public async Task<CycleJudgement> JudgeAsync(
        RedCommit red, TreeListing tree, ForkInventory fork, RedCycleHistory history, IReadOnlySet<string> structuralIds, CancellationToken cancellationToken)
    {
        if (tree.Outcome == TreeOutcome.CompileFailed)
        {
            return Judged(red, tree.Outcome, CheckVerdict.Fail, ReasonCode.CompileRed, $"COMPILE_RED: the tests do not compile at the red commit; declare the members they use as stubs. {tree.Detail}");
        }

        if (tree.Inventory is not IReadOnlyList<TestCase> inventory)
        {
            return NotJudged(red, tree, tree.Detail ?? "the tree could not be listed");
        }

        RedGreenRecord record = new()
        {
            BaselineCommit = red.ForkPoint ?? string.Empty,
            BaselineAt = time.GetUtcNow(),
            BaselineTests = fork.Tests,
            BaselineSkipped = fork.Skipped,
            BaselineExplicit = fork.Explicit,
            TestProjects = [],
            Cycles = [.. history.Accepted],
        };
        if (SuiteProblem(record, inventory) is string problem)
        {
            return Judged(red, tree.Outcome, CheckVerdict.Fail, ReasonCode.RedWeakensSuite, problem);
        }

        TestCase[] fresh = [.. inventory.Where(test => !record.BaselineTests.Contains(test.Key) && !history.EarlierNew.Contains(test.Key)
            && !Lifecycle.StructuralCheck.IsStructural(test.Requirements, structuralIds))];
        if (fresh.Length == 0)
        {
            return Judged(red, tree.Outcome, CheckVerdict.Fail, ReasonCode.RedAddsNoTests, "the red commit adds no tests; add the story's tests to it, or drop its trailer");
        }

        DateTimeOffset deadline = time.GetUtcNow().Add(trees.ProcessLimit);
        if (await tree.Workspace.RunAsync(fresh, cancellationToken) is not IReadOnlyList<ExecutedTest> results)
        {
            return Judged(red, tree.Outcome, CheckVerdict.Error, ReasonCode.ToolError, $"the test runner produced no report for {tree.Workspace.FailedApp}");
        }

        RedEvaluationResult evaluation = await RedEvaluation.ClassifyAsync(tree.Workspace, trees.Root, inventory, fresh, results, deadline, time, cancellationToken);
        RedGreenTest[] rejected = [.. evaluation.Classified.Where(test => !RedClassNames.IsAccepted(test.RedClass))];
        string why = rejected.Length == 0
            ? $"{evaluation.Classified.Count} new tests, each RED_STUB or RED_ASSERT"
            : $"{rejected.Length} of {evaluation.Classified.Count} new tests do not fail for the right reason: "
              + string.Join("; ", rejected.Select(test => test.RedClass).Distinct().Select(redClass => $"{RedClassNames.ToName(redClass)} ({RedClassFixes.For(redClass)})"));
        return new CycleJudgement(red, tree.Outcome, evaluation.Classified, evaluation.Verdicts, rejected.Length == 0 ? CheckVerdict.Pass : CheckVerdict.Fail,
            rejected.Length == 0 ? ReasonCode.None : CheckReasons.Of(rejected[0].RedClass), why)
        {
            Tampering = tree.Tampering,
            FingerprintsAtRed = fresh.Where(test => tree.Fingerprints.ContainsKey(test.Key)).ToDictionary(test => test.Key, test => tree.Fingerprints[test.Key], StringComparer.Ordinal),
            FlakyBudgetExhausted = evaluation.FlakyCheck.BudgetExhausted,
        };
    }

    /// <summary>A red whose tree, or whose fork point, could not be listed: an error when a tool broke, else not earned.</summary>
    public static CycleJudgement NotJudged(RedCommit red, TreeListing tree, string why) =>
        Judged(red, tree.Outcome, tree.Outcome == TreeOutcome.Broken ? CheckVerdict.Error : CheckVerdict.NotEarned, CheckReasons.NotListed(tree.Outcome), why);

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

    private static CycleJudgement Judged(RedCommit red, TreeOutcome build, CheckVerdict verdict, ReasonCode reason, string why) =>
        new(red, build, [], new Dictionary<string, RedVerdict>(StringComparer.Ordinal), verdict, reason, why);
}
