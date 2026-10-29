using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Green at head over the record the judge synthesizes from what it re-ran (the base's inventory, the accepted cycles), never the
/// tree's <c>redgreen.json</c>: every cycle test present and passing every row; no other failure beyond the base's known-failing
/// baseline; nothing deleted, skipped or made explicit since the base; the runner finished and every listed test that is not
/// explicit is in its report. A non-cycle test that passes only on a re-run is a warning; a cycle test that does is FLAKY, which
/// NOT-EARNS the check.
/// </summary>
internal static class GreenJudge
{
    private const int MaxListed = 5;

    public static GreenJudgement Decide(HeadRun head, IReadOnlyList<TestCase>? baseInventory, IReadOnlyList<CycleJudgement> cycles)
    {
        TreeListing listing = head.Listing;
        if (listing.Outcome == TreeOutcome.CompileFailed)
        {
            return Verdict(CheckVerdict.Fail, $"the head does not build: {listing.Detail}");
        }

        if (listing.Inventory is not IReadOnlyList<TestCase> inventory)
        {
            return Verdict(listing.Outcome == TreeOutcome.Broken ? CheckVerdict.Error : CheckVerdict.NotEarned, listing.Detail ?? "the head could not be listed");
        }

        if (head.Results is not IReadOnlyList<ExecutedTest> results)
        {
            return Verdict(CheckVerdict.Error, $"the test runner produced no report for {listing.Workspace.FailedApp}");
        }

        RedGreenTest[] cycleTests = [.. cycles.Where(cycle => cycle.Accepted).SelectMany(cycle => cycle.Tests)];
        RedGreenRecord record = Synthesized(baseInventory ?? inventory, cycles);
        RedGreenCycle all = new(0, DateTimeOffset.UnixEpoch, GreenAt: null, cycleTests);
        List<string> problems = [.. Incomplete(inventory, results, head.AbnormalExit)];
        problems.AddRange(SuiteChecks.GreenProblems(record, all, inventory, results, head.KnownFailing));
        HashSet<string> cycleKeys = [.. cycleTests.Select(test => test.Key)];
        string[] flakyCycle = [.. head.PassedOnRetry.Where(cycleKeys.Contains).Order(StringComparer.Ordinal)];
        string[] flakyOther = [.. head.PassedOnRetry.Where(key => !cycleKeys.Contains(key)).Order(StringComparer.Ordinal)];
        Dictionary<string, string> atHead = cycleTests.ToDictionary(test => test.Key, test => AtHead(test.Key, inventory, results, head.PassedOnRetry), StringComparer.Ordinal);
        HashSet<string> introduced = [.. cycles.SelectMany(cycle => cycle.Tests).Select(test => test.Key)];
        string[] withoutRed = baseInventory is null
            ? []
            : [.. inventory.Select(test => test.Key).Where(key => !record.BaselineTests.Contains(key) && !introduced.Contains(key)).Order(StringComparer.Ordinal)];
        int failed = results.Count(result => result.Outcome == TestOutcome.Fail);

        (CheckVerdict verdict, string why) = problems.Count > 0 ? (CheckVerdict.Fail, string.Join("; ", problems))
            : flakyCycle.Length > 0 ? (CheckVerdict.NotEarned, $"cycle tests passed only when run again (FLAKY): {string.Join(", ", flakyCycle.Take(MaxListed))}")
            : results.Count == 0 ? (CheckVerdict.NotEarned, "no test ran at head")
            : (CheckVerdict.Pass, $"{inventory.Count} tests, {results.Count} test cases run, {failed} failed"
                + (flakyOther.Length > 0 ? $"; passed only when run again: {string.Join(", ", flakyOther.Take(MaxListed))}" : string.Empty));
        return new GreenJudgement(verdict, why, results.Count, failed, problems, [.. head.PassedOnRetry.Order(StringComparer.Ordinal)], atHead, withoutRed);
    }

    /// <summary>The record of what the judge re-ran: the base inventory as the baseline, each accepted cycle closed at head.</summary>
    public static RedGreenRecord Synthesized(IReadOnlyList<TestCase> baseInventory, IReadOnlyList<CycleJudgement> cycles) => new()
    {
        BaselineCommit = string.Empty,
        BaselineAt = DateTimeOffset.UnixEpoch,
        BaselineTests = baseInventory.Select(test => test.Key).ToHashSet(StringComparer.Ordinal),
        BaselineSkipped = baseInventory.Where(test => test.SkipReason is not null).Select(test => test.Key).ToHashSet(StringComparer.Ordinal),
        BaselineExplicit = baseInventory.Where(test => test.IsExplicit).Select(test => test.Key).ToHashSet(StringComparer.Ordinal),
        TestProjects = [],
        Cycles = [.. cycles.Where(cycle => cycle.Accepted).Select((cycle, index) => new RedGreenCycle(index + 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, cycle.Tests) { Snapshot = cycle.Red.Commit })],
    };

    /// <summary>The runner ended abnormally, or listed tests that are not explicit are missing from its report.</summary>
    private static IEnumerable<string> Incomplete(IReadOnlyList<TestCase> inventory, IReadOnlyList<ExecutedTest> results, string? abnormalExit)
    {
        if (abnormalExit is not null)
        {
            yield return $"the test runner did not finish normally: {abnormalExit}";
        }

        HashSet<string> reported = [.. results.Select(result => result.Key)];
        string[] missing = [.. inventory.Where(test => !test.IsExplicit && !reported.Contains(test.Key)).Select(test => test.Key).Order(StringComparer.Ordinal)];
        if (missing.Length > 0)
        {
            yield return $"tests missing from the run's report (did the test host crash?): {string.Join(", ", missing.Take(MaxListed))}";
        }
    }

    private static string AtHead(string key, IReadOnlyList<TestCase> inventory, IReadOnlyList<ExecutedTest> results, IReadOnlySet<string> passedOnRetry)
    {
        TestCase? test = inventory.FirstOrDefault(candidate => candidate.Key == key);
        return test is null ? "missing"
            : test.IsExplicit ? "explicit"
            : test.SkipReason is not null || results.Any(result => result.Key == key && result.Outcome == TestOutcome.Skip) ? "skipped"
            : passedOnRetry.Contains(key) ? "flaky"
            : SuiteChecks.PassedEveryRow(results, key) ? "pass"
            : "fail";
    }

    private static GreenJudgement Verdict(CheckVerdict verdict, string why) =>
        new(verdict, why, 0, 0, [why], [], new Dictionary<string, string>(StringComparer.Ordinal), []);
}
