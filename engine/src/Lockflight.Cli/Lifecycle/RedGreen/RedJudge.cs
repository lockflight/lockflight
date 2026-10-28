using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// What a first red and the red after an amend judge alike: whether the suite lost or weakened tests, how each test failed and why
/// (shown under its line; <paramref name="verbose"/> adds the exception, the deciding frame and the production call), and the warning
/// for tests that assert nothing.
/// </summary>
internal sealed class RedJudge(RedGreenSession session, bool verbose)
{
    /// <summary>Indents the why lines under the test's name, past the class column.</summary>
    private const string WhyIndent = "             ";

    /// <summary>Whether the suite lost or weakened tests since the baseline and earlier cycles; says why when it did.</summary>
    public async Task<bool> SuiteProblemAsync(RedGreenRecord record, IReadOnlyList<TestCase> inventory, CancellationToken cancellationToken)
    {
        HashSet<string> current = [.. inventory.Select(test => test.Key)];
        string[] deleted = [.. record.KnownTests.Where(key => !current.Contains(key)).Order(StringComparer.Ordinal)];
        if (deleted.Length > 0)
        {
            await session.FailAsync($"tests disappeared since the baseline: {string.Join(", ", deleted.Take(RedGreenSession.MaxTestsListed))}; restore them (deleting tests is not part of a red)", cancellationToken);
            return true;
        }

        if (SuiteChecks.WeakenedKnownTests(record, inventory) is { Count: > 0 } weakened)
        {
            await session.FailAsync($"{string.Join("; ", weakened)}; restore them (a red adds tests, it does not switch existing ones off)", cancellationToken);
            return true;
        }

        return false;
    }

    public async Task WarnAssertionFreeAsync(List<RedGreenTest> classified)
    {
        string[] assertionFree = [.. classified.Where(test => test.AssertionFree).Select(test => TraceMatrix.ShortName(test.MethodFilter))];
        if (assertionFree.Length > 0)
        {
            await session.Output.WriteLineAsync(
                $"Warning: {assertionFree.Length} new tests assert nothing ({RedGreenTest.AssertionFreeMark}): {string.Join(", ", assertionFree.Take(RedGreenSession.MaxTestsListed))}. " +
                "Such a test passes as soon as the code returns without throwing. Add an assertion on the result; " +
                "a test that verifies by letting the production code throw may keep this warning, which trace and review show.");
        }
    }

    /// <summary>Why the test got its class, under its line; with <c>--verbose</c> also the exception, the deciding frame and the production call.</summary>
    public async Task WriteWhyAsync(RedVerdict verdict)
    {
        await session.Output.WriteLineAsync($"{WhyIndent}{verdict.Why}");
        if (verbose)
        {
            await session.Output.WriteLineAsync($"{WhyIndent}{verdict.Detail}");
        }
    }

    /// <summary>
    /// Classifies each new test by how it failed, with why; tests that passed every time they ran are re-run together to tell flaky
    /// from green.
    /// </summary>
    public async Task<(List<RedGreenTest> Classified, FlakyCheckResult FlakyCheck, IReadOnlyDictionary<string, RedVerdict> Verdicts)> ClassifyAsync(
        TestWorkspace workspace,
        string root,
        IReadOnlyList<TestCase> inventory,
        TestCase[] fresh,
        IReadOnlyList<ExecutedTest> results,
        DateTimeOffset deadline,
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
            : await new FlakyCheck(workspace.RunAsync, session.Context.Time).RunAsync(candidates, deadline, cancellationToken);

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
        return (classified, flakyCheck, verdicts);
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
