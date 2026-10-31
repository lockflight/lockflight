using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Green at head over the record the judge synthesizes from what it re-ran (the base's inventory, the accepted cycles), never the
/// tree's <c>redgreen.json</c>: every cycle test present and passing every row; no other failure beyond the base's known-failing
/// baseline; nothing deleted, skipped or made explicit since the base; the runner finished and every listed test that is not
/// explicit is in its report. A cycle test missing at head (deleted or renamed after its red) is NOT-EARNED, not a deleted test:
/// the proof is gone, and tests since red says how to keep it. A base test skipped at run time at head (<c>Assert.Skip</c>,
/// <c>Skip.If</c>, "no Docker here") is a new skip only for the rows the same test, run on the base, does not skip with the same
/// message: the base is listed, not run, so those tests alone are run there (<see cref="SkippedAtRunTime"/>); when the base cannot
/// run them the check is NOT-EARNED. A non-cycle test that passes only on a re-run is a warning; a cycle test that does is FLAKY, which
/// NOT-EARNS the check.
/// </summary>
internal static class GreenJudge
{
    private const int MaxListed = 5;

    public static GreenJudgement Decide(
        HeadRun head, IReadOnlyList<TestCase>? baseInventory, BaseSkipCheck baseSkips, IReadOnlyList<CycleJudgement> cycles)
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

        HashSet<string> listed = [.. inventory.Select(test => test.Key)];
        string[] missingCycle = [.. cycles.Where(cycle => cycle.Accepted).SelectMany(cycle => cycle.Tests).Select(test => test.Key).Where(key => !listed.Contains(key)).Order(StringComparer.Ordinal)];
        IReadOnlyList<CycleJudgement> present = [.. cycles.Select(cycle => cycle with { Tests = [.. cycle.Tests.Where(test => listed.Contains(test.Key))] })];
        RedGreenTest[] cycleTests = [.. present.Where(cycle => cycle.Accepted).SelectMany(cycle => cycle.Tests)];

        // A test the base could not run to compare is left out of the new skips here and NOT-EARNS the check below, never FAILs it.
        Dictionary<string, int> waived = new(baseSkips.MatchedRows, StringComparer.Ordinal);
        foreach (string key in baseSkips.Unchecked)
        {
            waived[key] = int.MaxValue;
        }

        RedGreenRecord record = Synthesized(baseInventory ?? inventory, present) with { BaselineSkippedRows = waived };
        RedGreenCycle all = new(0, DateTimeOffset.UnixEpoch, GreenAt: null, cycleTests);
        List<string> problems = [.. Incomplete(inventory, results, head.AbnormalExit)];
        problems.AddRange(SuiteChecks.GreenProblems(record, all, inventory, results, head.KnownFailing));
        HashSet<string> cycleKeys = [.. cycleTests.Select(test => test.Key)];
        string[] flakyCycle = [.. head.PassedOnRetry.Where(cycleKeys.Contains).Order(StringComparer.Ordinal)];
        string[] flakyOther = [.. head.PassedOnRetry.Where(key => !cycleKeys.Contains(key)).Order(StringComparer.Ordinal)];
        Dictionary<string, string> atHead = missingCycle.ToDictionary(key => key, _ => "missing", StringComparer.Ordinal);
        foreach (RedGreenTest test in cycleTests)
        {
            atHead[test.Key] = AtHead(test.Key, inventory, results, head.PassedOnRetry);
        }

        HashSet<string> introduced = [.. cycles.SelectMany(cycle => cycle.Tests).Select(test => test.Key)];
        string[] withoutRed = baseInventory is null
            ? []
            : [.. inventory.Select(test => test.Key).Where(key => !record.BaselineTests.Contains(key) && !introduced.Contains(key)).Order(StringComparer.Ordinal)];
        int failed = results.Count(result => result.Outcome == TestOutcome.Fail);

        string notCompared = baseSkips.Unchecked.Count > 0 ? $" (run-time skips not compared on the base: {string.Join(", ", baseSkips.Unchecked.Take(MaxListed))})" : string.Empty;
        (CheckVerdict verdict, string why) = problems.Count > 0 ? (CheckVerdict.Fail, string.Join("; ", problems) + notCompared)
            : missingCycle.Length > 0 ? (CheckVerdict.NotEarned, $"cycle tests missing at head: {string.Join(", ", missingCycle.Take(MaxListed))}; {SinceRedJudge.MissingWhy}")
            : baseSkips.Failure is string failure && baseSkips.Unchecked.Count > 0
                ? (CheckVerdict.NotEarned, $"tests skipped at run time at head could not be run on the base to compare ({failure}): {string.Join(", ", baseSkips.Unchecked.Take(MaxListed))}")
            : flakyCycle.Length > 0 ? (CheckVerdict.NotEarned, $"cycle tests passed only when run again (FLAKY): {string.Join(", ", flakyCycle.Take(MaxListed))}")
            : results.Count == 0 ? (CheckVerdict.NotEarned, "no test ran at head")
            : (CheckVerdict.Pass, $"{inventory.Count} tests, {results.Count} test cases run, {failed} failed"
                + (flakyOther.Length > 0 ? $"; passed only when run again: {string.Join(", ", flakyOther.Take(MaxListed))}" : string.Empty));
        return new GreenJudgement(verdict, why, results.Count, failed, problems, [.. head.PassedOnRetry.Order(StringComparer.Ordinal)], atHead, withoutRed);
    }

    /// <summary>
    /// The base's tests that head skipped at run time, not skipped or explicit in the base's listing: the ones to run on the base to
    /// learn whether they skip there too. Empty when head did not run or the base was not listed.
    /// </summary>
    public static IReadOnlyList<TestCase> SkippedAtRunTime(HeadRun head, IReadOnlyList<TestCase>? baseInventory)
    {
        if (head.Results is not IReadOnlyList<ExecutedTest> results || baseInventory is null)
        {
            return [];
        }

        HashSet<string> skipped = [.. SuiteChecks.SkippedRows(results).Keys];
        return [.. baseInventory.Where(test => skipped.Contains(test.Key) && test.SkipReason is null && !test.IsExplicit)];
    }

    /// <summary>
    /// Per test, how many of head's skipped rows have a skipped row on the base with the same skip message (ordinal, trimmed), each
    /// base row matched once; tests with none are left out.
    /// </summary>
    public static IReadOnlyDictionary<string, int> MatchedSkips(IReadOnlyList<ExecutedTest> head, IReadOnlyList<ExecutedTest> baseResults)
    {
        Dictionary<string, int> matched = new(StringComparer.Ordinal);
        foreach (IGrouping<string, ExecutedTest> rows in head.Where(result => result.Outcome == TestOutcome.Skip).GroupBy(result => result.Key, StringComparer.Ordinal))
        {
            List<string> baseReasons = [.. baseResults.Where(result => result.Key == rows.Key && result.Outcome == TestOutcome.Skip).Select(result => Reason(result) ?? string.Empty)];
            int count = rows.Count(row => Reason(row) is string reason && baseReasons.Remove(reason));
            if (count > 0)
            {
                matched[rows.Key] = count;
            }
        }

        return matched;
    }

    /// <summary>
    /// The comparison of the run-time skips the base was asked to run (<paramref name="keys"/>): a test missing from the base's
    /// report, or a skipped row on either side without a message, cannot be compared and is NOT-EARNED, not matched.
    /// </summary>
    public static BaseSkipCheck CompareSkips(IReadOnlyList<ExecutedTest> head, IReadOnlyList<ExecutedTest> baseResults, IReadOnlyList<string> keys)
    {
        string[] missing = [.. keys.Where(key => !baseResults.Any(result => result.Key == key))];
        string[] silent = [.. keys.Except(missing, StringComparer.Ordinal).Where(key => head.Concat(baseResults)
            .Any(result => result.Key == key && result.Outcome == TestOutcome.Skip && Reason(result) is null))];
        string[] uncomparable = [.. missing, .. silent];
        string? failure = missing.Length > 0 ? "not in the base run's report"
            : silent.Length > 0 ? "the runner reported no skip message to compare" : null;
        HashSet<string> left = new(uncomparable, StringComparer.Ordinal);
        Dictionary<string, int> matched = MatchedSkips(head, baseResults).Where(pair => !left.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal);
        return new BaseSkipCheck(matched, uncomparable, failure);
    }

    private static string? Reason(ExecutedTest result) => result.SkipReason?.Trim() is { Length: > 0 } reason ? reason : null;

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
