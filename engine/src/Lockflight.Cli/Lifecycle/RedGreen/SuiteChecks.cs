using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// What the test suite may not lose between the baseline and a green: no test deleted, newly skipped (<c>Skip = "…"</c>,
/// <c>[Ignore]</c>, or at run time: <c>Assert.Skip</c>, <c>Assert.Ignore</c>, an ignored row) or newly marked explicit
/// (<c>Explicit = true</c>, <c>[Explicit]</c>). Explicit tests are read from the runner's listing (<c>"Explicit": true</c>
/// in <c>-list full/json</c>, <c>Explicit: true</c> in MTP's text listing) and compared with the explicit set recorded at <c>start</c>.
/// Run outcomes cannot tell them apart: a normal run reports every explicit test as NotRun, whether it was always explicit or was
/// marked explicit to dodge a failure. The listing is the runner's own view of the compiled tests, so it also sees an
/// <c>Explicit</c> set through a custom attribute or a constant — which a source search would miss.
/// </summary>
internal static class SuiteChecks
{
    private const int MaxTestsListed = 5;

    /// <summary>Why the open cycle cannot close: its tests not passing, other new failures, deleted tests, new skips or new explicit tests.</summary>
    public static List<string> GreenProblems(
        RedGreenRecord record, RedGreenCycle open, IReadOnlyList<TestCase> inventory, IReadOnlyList<ExecutedTest> results, IReadOnlySet<string> knownFailing)
    {
        HashSet<string> current = [.. inventory.Select(test => test.Key)];
        string[] deleted = [.. record.KnownTests.Where(key => !current.Contains(key)).Order(StringComparer.Ordinal)];
        string[] cycleNotPassing = [.. open.Tests.Where(test => !PassedEveryRow(results, test.Key)).Select(test => test.MethodFilter)];
        string[] otherFailures = [.. results
            .Where(result => result.Outcome == TestOutcome.Fail && open.Tests.All(test => test.Key != result.Key) && !knownFailing.Contains(result.Name))
            .Select(result => result.Name)
            .Distinct()];
        // NotRun is not counted as a skip: explicit tests are never run in a full run. They are caught from the listing instead, and
        // so are NUnit's, which its TRX reports as not executed, like a skipped test.
        // A test with rows skipped at run time is a new skip when more of its rows are skipped than at the baseline.
        HashSet<string> explicitNow = [.. inventory.Where(test => test.IsExplicit).Select(test => test.Key)];
        HashSet<string> skippedNow = [.. inventory.Where(test => test.SkipReason is not null).Select(test => test.Key),
            .. SkippedRows(results).Where(rows => !explicitNow.Contains(rows.Key) && rows.Value > record.BaselineSkippedRows.GetValueOrDefault(rows.Key)).Select(rows => rows.Key)];
        string[] newSkips = [.. skippedNow.Where(key => !record.BaselineSkipped.Contains(key)).Order(StringComparer.Ordinal)];

        List<string> problems = [];
        AddProblem(problems, "cycle tests not passing", cycleNotPassing);
        AddProblem(problems, "other tests fail", otherFailures);
        AddProblem(problems, "tests deleted", deleted);
        AddProblem(problems, "new skipped tests", newSkips);
        AddProblem(problems, "tests newly marked Explicit (a normal run never executes them)", NewlyExplicit(record, inventory));
        return problems;
    }

    /// <summary>
    /// Known tests (baseline and earlier cycles) skipped by attribute or marked explicit since the baseline; checked before a red so
    /// a weakened suite is reported at once rather than at the next green. Runtime skips need a run and are left to the green.
    /// </summary>
    public static List<string> WeakenedKnownTests(RedGreenRecord record, IReadOnlyList<TestCase> inventory)
    {
        IReadOnlySet<string> known = record.KnownTests;
        TestCase[] knownNow = [.. inventory.Where(test => known.Contains(test.Key))];
        string[] newSkips = [.. knownNow.Where(test => test.SkipReason is not null && !record.BaselineSkipped.Contains(test.Key)).Select(test => test.Key).Order(StringComparer.Ordinal)];

        List<string> problems = [];
        AddProblem(problems, "known tests newly skipped", newSkips);
        AddProblem(problems, "known tests newly marked Explicit", NewlyExplicit(record, knownNow));
        return problems;
    }

    /// <summary>
    /// Known tests a green's regression pass left out although no earlier pass left them out (the baseline's, or an earlier green's):
    /// a trait or a stop filter added since must not take a test that ran out of the green, as <c>Explicit</c> must not.
    /// </summary>
    public static string[] NewlyLeftOut(RedGreenRecord record, IReadOnlySet<string> excludedNow)
    {
        HashSet<string> leftOutBefore = [.. new[] { record.BaselineRegressionFilter }.Concat(record.Cycles.Select(cycle => cycle.RegressionFilter))
            .SelectMany(filter => filter?.ExcludedTests ?? [])];
        // The open cycle's own tests are judged by the green's fail-fast run, filter or not.
        HashSet<string> open = [.. record.OpenCycle?.Tests.Select(test => test.Key) ?? []];
        IReadOnlySet<string> known = record.KnownTests;
        return [.. excludedNow.Where(key => known.Contains(key) && !open.Contains(key) && !leftOutBefore.Contains(key)).Order(StringComparer.Ordinal)];
    }

    /// <summary>How many rows of each test a run skipped (<c>Assert.Skip</c>, <c>Assert.Ignore</c>, an ignored row); tests without any are left out.</summary>
    public static IReadOnlyDictionary<string, int> SkippedRows(IReadOnlyList<ExecutedTest> results) =>
        results.Where(result => result.Outcome == TestOutcome.Skip).GroupBy(result => result.Key, StringComparer.Ordinal)
            .ToDictionary(rows => rows.Key, rows => rows.Count(), StringComparer.Ordinal);

    /// <summary>A test ran (every theory row) and passed; one that did not run at all has not passed.</summary>
    public static bool PassedEveryRow(IReadOnlyList<ExecutedTest> results, string key)
    {
        ExecutedTest[] rows = [.. results.Where(result => result.Key == key)];
        return rows.Length > 0 && rows.All(row => row.Outcome == TestOutcome.Pass);
    }

    private static string[] NewlyExplicit(RedGreenRecord record, IEnumerable<TestCase> tests) =>
        [.. tests.Where(test => test.IsExplicit && !record.BaselineExplicit.Contains(test.Key)).Select(test => test.Key).Order(StringComparer.Ordinal)];

    private static void AddProblem(List<string> problems, string label, string[] names)
    {
        if (names.Length > 0)
        {
            problems.Add($"{label}: {string.Join(", ", names.Take(MaxTestsListed))}");
        }
    }
}
