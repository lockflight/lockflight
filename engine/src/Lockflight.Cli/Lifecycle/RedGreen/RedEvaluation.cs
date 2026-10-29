using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// The classification of a red, without a session, a ledger or output: how each new test failed (<see cref="RedClassifier"/>), which
/// production member it reaches and whether it asserts nothing (read from the built test assemblies), and FLAKY for a test that
/// passed and then failed on a re-run. <c>redgreen red</c> and the CI judge both call it, so a red accepted locally is judged by the
/// same code in CI.
/// </summary>
internal static class RedEvaluation
{
    public static async Task<RedEvaluationResult> ClassifyAsync(
        TestWorkspace workspace,
        string root,
        IReadOnlyList<TestCase> inventory,
        TestCase[] fresh,
        IReadOnlyList<ExecutedTest> results,
        DateTimeOffset deadline,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<string> testClasses = inventory.Select(test => test.Class).ToHashSet(StringComparer.Ordinal);
        string[] testDirectories = [.. workspace.Apps.Select(app => app.ProjectDirectory)];
        ProductionCode production = ProductionProjects.Find(root, workspace.Apps);
        NewTestScan scan = Scan(workspace, fresh, production);
        List<(TestCase Test, RedVerdict Verdict)> first = [.. fresh.Select(test => (test, RedClassifier.Judge(
            [.. results.Where(result => result.Key == test.Key)], testClasses, testDirectories, production, scan.ProductionCalls.GetValueOrDefault(test.Key))))];

        // Only a test whose rows all passed is re-run: a theory with mixed rows is GREEN_BEFORE, not flaky.
        TestCase[] candidates = [.. first.Where(entry => entry.Verdict.Class == RedClass.GreenBefore && SuiteChecks.PassedEveryRow(results, entry.Test.Key)).Select(entry => entry.Test)];
        FlakyCheckResult flakyCheck = candidates.Length == 0
            ? new FlakyCheckResult(new HashSet<string>(StringComparer.Ordinal), 0, BudgetExhausted: false)
            : await new FlakyCheck(workspace.RunAsync, time).RunAsync(candidates, deadline, cancellationToken);

        List<RedGreenTest> classified = [.. first.Select(entry => new RedGreenTest(
            entry.Test.Key,
            entry.Test.MethodFilter,
            flakyCheck.Flaky.Contains(entry.Test.Key) ? RedClass.Flaky : entry.Verdict.Class,
            entry.Test.Requirements)
        {
            AssertionFree = scan.AssertionFree.Contains(entry.Test.Key),
            Rows = results.Count(result => result.Key == entry.Test.Key),
        })];
        Dictionary<string, RedVerdict> verdicts = first.ToDictionary(
            entry => entry.Test.Key,
            entry => flakyCheck.Flaky.Contains(entry.Test.Key) ? entry.Verdict with { Class = RedClass.Flaky, Why = "passed, then failed when run again" } : entry.Verdict,
            StringComparer.Ordinal);
        return new RedEvaluationResult(classified, flakyCheck, verdicts);
    }

    /// <summary>Which production member each new test reaches first, and which assert nothing, read from the built test assemblies.</summary>
    private static NewTestScan Scan(TestWorkspace workspace, TestCase[] fresh, ProductionCode production)
    {
        Dictionary<string, string> calls = new(StringComparer.Ordinal);
        HashSet<string> assertionFree = new(StringComparer.Ordinal);
        foreach (TestApp app in workspace.Apps)
        {
            TestCase[] tests = [.. fresh.Where(test => test.Assembly == app.AssemblyName)];
            if (tests.Length == 0)
            {
                continue;
            }

            using TestCodeScanner scanner = new(app.AssemblyPath, production.AssemblyNames);
            foreach (TestCase test in tests)
            {
                if (scanner.ProductionCall(test.Class, test.Method) is string call)
                {
                    calls[test.Key] = call;
                }
            }

            assertionFree.UnionWith(tests.Where(test => scanner.AssertsNothing(test.Class, test.Method)).Select(test => test.Key));
        }

        return new NewTestScan(calls, assertionFree);
    }
}
