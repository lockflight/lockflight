using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// <c>lockflight redgreen green</c>: closes the open cycle when its tests pass with nothing deleted, skipped, made explicit or broken
/// (<see cref="SuiteChecks"/>), the runner finished normally and every known test actually ran — a run that crashed half-way, with
/// the rest of the suite missing from its report, is not a green. The locked test files must be the ones the red accepted, or the
/// ones an amend's red re-checked (<see cref="TestFileHashes"/>).
/// </summary>
internal sealed class GreenStep(RedGreenSession session)
{
    public async Task<ExitCode> RunAsync(RedGreenRecord? record, CancellationToken cancellationToken)
    {
        if (record?.OpenCycle is not RedGreenCycle open)
        {
            return await session.FailAsync($"no red cycle is open; run '{EngineCommand.Linked} redgreen red' after writing failing tests", cancellationToken);
        }

        if (open.OpenAmends.ToArray() is { Length: > 0 } waiting)
        {
            string amended = string.Join(", ", waiting.Select(amend => amend.MethodFilter));
            return await session.FailAsync(
                $"{amended} {(waiting.Length == 1 ? "was" : "were")} amended and not re-checked; run '{EngineCommand.Linked} redgreen red' first: the amended tests must fail in production code again before a green",
                cancellationToken);
        }

        if (TestFileHashes.Refusal(session.Manifest.Root, record) is string changed)
        {
            return await RejectAsync(open, [changed], cancellationToken);
        }

        if (session.UnapprovedArtifacts() is string unapproved)
        {
            return await session.FailAsync(unapproved, cancellationToken);
        }

        TestWorkspace workspace = session.Workspace;
        if (await workspace.BuildAsync(cancellationToken) is string buildOutput)
        {
            return await session.FailAsync($"the build fails.\n{RedGreenSession.Tail(buildOutput)}", cancellationToken);
        }

        if (workspace.DiscoveryProblem is string incomplete)
        {
            return await session.NotEarnedAsync($"the green cannot be judged, the cycle stays open: {incomplete}", cancellationToken);
        }

        if (await workspace.ListAsync(cancellationToken) is not IReadOnlyList<TestCase> inventory)
        {
            return await session.BrokenAsync($"the test runner could not list the tests of {workspace.FailedApp}", cancellationToken);
        }

        // Fail fast: the cycle's own tests first, so a still-stubbed green does not wait for the whole suite.
        TestCase[] cycleTests = [.. inventory.Where(test => open.Tests.Any(cycleTest => cycleTest.Key == test.Key))];
        if (cycleTests.Length > 0 && await workspace.RunAsync(cycleTests, cancellationToken) is IReadOnlyList<ExecutedTest> cycleResults
            && cycleTests.Where(test => !SuiteChecks.PassedEveryRow(cycleResults, test.Key)).Select(test => test.MethodFilter).ToArray() is { Length: > 0 } stillFailing)
        {
            return await RejectAsync(open, [$"cycle tests not passing: {string.Join(", ", stillFailing.Take(RedGreenSession.MaxTestsListed))} (the rest of the suite was not run)"], cancellationToken);
        }

        if (await workspace.RunAsync(null, cancellationToken) is not IReadOnlyList<ExecutedTest> results)
        {
            return await session.BrokenAsync($"the test runner could not run the tests of {workspace.FailedApp}", cancellationToken);
        }

        List<string> problems = [.. IncompleteRun(record, inventory, results, workspace.AbnormalExit)];
        problems.AddRange(SuiteChecks.GreenProblems(record, open, inventory, results, KnownFailingTests.LoadAll(session.Manifest)));
        if (problems.Count > 0)
        {
            return await RejectAsync(open, problems, cancellationToken);
        }

        session.Save(record with { Cycles = [.. record.Cycles.SkipLast(1), open with { GreenAt = session.Context.Time.GetUtcNow() }] });
        await session.RecordAsync(LedgerSignatures.GreenAccepted, $"{session.Feature.Id}: cycle {open.Number}", cancellationToken);
        await session.Output.WriteLineAsync($"Green: cycle {open.Number} closed — {open.Tests.Count} tests pass, {results.Count} test cases run, nothing deleted, skipped or made explicit. Test projects unlocked.");
        return ExitCode.Pass;
    }

    /// <summary>
    /// Why the full run cannot vouch for the suite: the runner ended abnormally, or known tests that are still in the inventory (and
    /// not explicit, which a normal run never executes) are missing from its report.
    /// </summary>
    public static IEnumerable<string> IncompleteRun(RedGreenRecord record, IReadOnlyList<TestCase> inventory, IReadOnlyList<ExecutedTest> results, string? abnormalExit)
    {
        if (abnormalExit is not null)
        {
            yield return $"the test runner did not finish normally: {abnormalExit}";
        }

        HashSet<string> reported = [.. results.Select(result => result.Key)];
        IReadOnlySet<string> known = record.KnownTests;
        string[] missing = [.. inventory
            .Where(test => !test.IsExplicit && known.Contains(test.Key) && !reported.Contains(test.Key))
            .Select(test => test.Key)
            .Order(StringComparer.Ordinal)];
        if (missing.Length > 0)
        {
            yield return $"known tests missing from the run's report (did the test host crash?): {string.Join(", ", missing.Take(RedGreenSession.MaxTestsListed))}";
        }
    }

    private async Task<ExitCode> RejectAsync(RedGreenCycle open, IReadOnlyList<string> problems, CancellationToken cancellationToken)
    {
        foreach (string problem in problems)
        {
            await session.Output.WriteLineAsync($"FAIL {problem}");
        }

        await session.RecordAsync(LedgerSignatures.GreenRejected, $"{session.Feature.Id}: cycle {open.Number}", cancellationToken);
        return ExitCode.Fail;
    }
}
