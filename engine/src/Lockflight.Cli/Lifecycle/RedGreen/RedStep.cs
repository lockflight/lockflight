using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// <c>lockflight redgreen red</c>: accepts the new tests of a cycle when they all fail in production code (RED_STUB, or RED_ASSERT
/// in a test that calls production code), which opens the cycle and locks the test projects. A new test whose code asserts nothing
/// is flagged assertion-free but not rejected: a test may verify by letting the production code throw (a guard clause, a
/// validator), which a static reading of the test code cannot tell from a test that verifies nothing.
/// </summary>
internal sealed class RedStep(RedGreenSession session)
{
    public async Task<ExitCode> RunAsync(RedGreenRecord? record, CancellationToken cancellationToken)
    {
        if (record is null)
        {
            return await session.FailAsync($"no baseline; run '{EngineCommand.Linked} redgreen start' before writing the feature's tests", cancellationToken);
        }

        if (record.OpenCycle is RedGreenCycle open)
        {
            return await session.FailAsync($"cycle {open.Number} is still red; make it green ('{EngineCommand.Linked} redgreen green') before the next red", cancellationToken);
        }

        if (session.UnapprovedArtifacts() is string unapproved)
        {
            return await session.FailAsync(unapproved, cancellationToken);
        }

        // The FLAKY re-runs must end within gates.timeoutSeconds of the red's start (the build, listing and first run count against
        // it), so a big cycle cannot run for ages; each of those earlier steps is bounded by the per-process limit as before.
        DateTimeOffset deadline = session.Context.Time.GetUtcNow().AddSeconds(session.Manifest.Gates.TimeoutSeconds);
        TestWorkspace workspace = session.Workspace;
        if (await workspace.BuildAsync(cancellationToken) is string buildOutput)
        {
            await session.Output.WriteLineAsync($"COMPILE_RED the tests do not compile; declare the members they use as stubs ('throw new NotImplementedException();').\n{RedGreenSession.Tail(buildOutput)}");
            await session.RecordAsync(LedgerSignatures.RedCompileError, session.Feature.Id, cancellationToken);
            return ExitCode.Fail;
        }

        if (await workspace.ListAsync(cancellationToken) is not IReadOnlyList<TestCase> inventory)
        {
            return await session.BrokenAsync($"the test runner could not list the tests of {workspace.FailedApp}", cancellationToken);
        }

        if (await NewTestsAsync(record, inventory, cancellationToken) is not TestCase[] fresh)
        {
            return ExitCode.Fail;
        }

        if (await workspace.RunAsync(fresh, cancellationToken) is not IReadOnlyList<ExecutedTest> results)
        {
            return await session.BrokenAsync($"the test runner produced no report for {workspace.FailedApp}", cancellationToken);
        }

        return await JudgeAsync(record, inventory, fresh, results, deadline, cancellationToken);
    }

    /// <summary>The tests added since the baseline and earlier cycles; null — after saying why — when the suite lost or weakened tests, or nothing is new.</summary>
    private async Task<TestCase[]?> NewTestsAsync(RedGreenRecord record, IReadOnlyList<TestCase> inventory, CancellationToken cancellationToken)
    {
        HashSet<string> current = [.. inventory.Select(test => test.Key)];
        IReadOnlySet<string> known = record.KnownTests;
        string[] deleted = [.. known.Where(key => !current.Contains(key)).Order(StringComparer.Ordinal)];
        if (deleted.Length > 0)
        {
            await session.FailAsync($"tests disappeared since the baseline: {string.Join(", ", deleted.Take(RedGreenSession.MaxTestsListed))}; restore them (deleting tests is not part of a red)", cancellationToken);
            return null;
        }

        if (SuiteChecks.WeakenedKnownTests(record, inventory) is { Count: > 0 } weakened)
        {
            await session.FailAsync($"{string.Join("; ", weakened)}; restore them (a red adds tests, it does not switch existing ones off)", cancellationToken);
            return null;
        }

        TestCase[] fresh = [.. inventory.Where(test => !known.Contains(test.Key))];
        if (fresh.Length == 0)
        {
            await session.FailAsync("no new tests since the baseline and earlier cycles; write the story's tests first", cancellationToken);
            return null;
        }

        return fresh;
    }

    private async Task<ExitCode> JudgeAsync(
        RedGreenRecord record, IReadOnlyList<TestCase> inventory, TestCase[] fresh, IReadOnlyList<ExecutedTest> results, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        (List<RedGreenTest> classified, FlakyCheckResult flakyCheck) = await ClassifyAsync(inventory, fresh, results, deadline, cancellationToken);
        foreach (RedGreenTest test in classified)
        {
            string requirements = test.Requirements.Count > 0 ? string.Join(",", test.Requirements) : "no Requirement trait";
            string mark = test.AssertionFree ? $" {RedGreenTest.AssertionFreeMark}" : string.Empty;
            await session.Output.WriteLineAsync($"{RedClassNames.ToName(test.RedClass),-12} {test.MethodFilter} ({requirements}){mark}");
        }

        await WarnAssertionFreeAsync(classified);

        if (flakyCheck.BudgetExhausted)
        {
            await session.Output.WriteLineAsync(
                $"Note: the FLAKY check stopped after {flakyCheck.ReRuns} of {FlakyCheck.MaxReRuns} re-runs — the red's time budget (gates.timeoutSeconds = {session.Manifest.Gates.TimeoutSeconds} s) ran out. " +
                "The GREEN_BEFORE tests may also be flaky; either way they passed before the implementation existed, so the red is rejected.");
        }

        RedGreenTest[] rejected = [.. classified.Where(test => !RedClassNames.IsAccepted(test.RedClass))];
        if (rejected.Length > 0)
        {
            await session.Output.WriteLineAsync($"Red rejected: {rejected.Length} of {classified.Count} new tests do not fail for the right reason. A red must be RED_STUB or RED_ASSERT.");
            foreach (RedClass redClass in rejected.Select(test => test.RedClass).Distinct())
            {
                await session.Output.WriteLineAsync($"  {RedClassNames.ToName(redClass)}: {RedClassFixes.For(redClass)}.");
            }

            await session.RecordAsync(LedgerSignatures.RedRejected, $"{session.Feature.Id}: {string.Join(",", rejected.Select(test => RedClassNames.ToName(test.RedClass)).Distinct())}", cancellationToken);
            return ExitCode.Fail;
        }

        return await AcceptAsync(record, classified, cancellationToken);
    }

    private async Task WarnAssertionFreeAsync(List<RedGreenTest> classified)
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

    /// <summary>Classifies each new test by how it failed; tests that passed every time they ran are re-run together to tell flaky from green.</summary>
    private async Task<(List<RedGreenTest> Classified, FlakyCheckResult FlakyCheck)> ClassifyAsync(
        IReadOnlyList<TestCase> inventory, TestCase[] fresh, IReadOnlyList<ExecutedTest> results, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        TestWorkspace workspace = session.Workspace;
        IReadOnlySet<string> testClasses = inventory.Select(test => test.Class).ToHashSet(StringComparer.Ordinal);
        string[] testDirectories = [.. workspace.Apps.Select(app => app.ProjectDirectory)];
        ProductionCode production = ProductionProjects.Find(session.Manifest.Root, workspace.Apps);
        NewTestScan scan = Scan(fresh, production);
        List<(TestCase Test, RedClass RedClass)> first = [.. fresh.Select(test => (test, RedClassifier.Classify(
            [.. results.Where(result => result.Key == test.Key)], testClasses, testDirectories, production, scan.CallsProduction.Contains(test.Key))))];

        // Only a test whose rows all passed is re-run: a theory with mixed rows is GREEN_BEFORE, not flaky.
        TestCase[] candidates = [.. first.Where(entry => entry.RedClass == RedClass.GreenBefore && SuiteChecks.PassedEveryRow(results, entry.Test.Key)).Select(entry => entry.Test)];
        FlakyCheckResult flakyCheck = candidates.Length == 0
            ? new FlakyCheckResult(new HashSet<string>(StringComparer.Ordinal), 0, BudgetExhausted: false)
            : await new FlakyCheck(workspace.RunAsync, session.Context.Time).RunAsync(candidates, deadline, cancellationToken);

        List<RedGreenTest> classified = [.. first.Select(entry => new RedGreenTest(
            entry.Test.Key,
            entry.Test.MethodFilter,
            flakyCheck.Flaky.Contains(entry.Test.Key) ? RedClass.Flaky : entry.RedClass,
            entry.Test.Requirements) { AssertionFree = scan.AssertionFree.Contains(entry.Test.Key) })];
        return (classified, flakyCheck);
    }

    /// <summary>Which new tests call production code and which assert nothing, read from the built test assemblies.</summary>
    private NewTestScan Scan(TestCase[] fresh, ProductionCode production)
    {
        HashSet<string> callers = new(StringComparer.Ordinal);
        HashSet<string> assertionFree = new(StringComparer.Ordinal);
        foreach (TestApp app in session.Workspace.Apps)
        {
            TestCase[] tests = [.. fresh.Where(test => test.Assembly == app.AssemblyName)];
            if (tests.Length == 0)
            {
                continue;
            }

            using TestCodeScanner scanner = new(app.AssemblyPath, production.AssemblyNames);
            callers.UnionWith(tests.Where(test => scanner.CallsProduction(test.Class, test.Method)).Select(test => test.Key));
            assertionFree.UnionWith(tests.Where(test => scanner.AssertsNothing(test.Class, test.Method)).Select(test => test.Key));
        }

        return new NewTestScan(callers, assertionFree);
    }

    private async Task<ExitCode> AcceptAsync(RedGreenRecord record, List<RedGreenTest> classified, CancellationToken cancellationToken)
    {
        RedGreenCycle cycle = new(record.Cycles.Count + 1, session.Context.Time.GetUtcNow(), GreenAt: null, classified);
        RedGreenRecord accepted = record with
        {
            // Test projects added since the start are locked too.
            TestProjects = [.. record.TestProjects.Union(session.RelativeProjects(), StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            Cycles = [.. record.Cycles, cycle],
        };
        session.Save(accepted);
        await session.RecordAsync(LedgerSignatures.RedAccepted, $"{session.Feature.Id}: cycle {cycle.Number}, {classified.Count} tests", cancellationToken);
        await session.Output.WriteLineAsync($"Red accepted: cycle {cycle.Number} with {classified.Count} tests. The test projects ({string.Join(", ", accepted.TestProjects)}) are locked until '{EngineCommand.Linked} redgreen green' passes.");
        if (classified.Any(test => test.Requirements.Count == 0))
        {
            await session.Output.WriteLineAsync("Note: tests without [Trait(\"Requirement\", \"FR-xxx\")] will not count for trace.");
        }

        return ExitCode.Pass;
    }
}
