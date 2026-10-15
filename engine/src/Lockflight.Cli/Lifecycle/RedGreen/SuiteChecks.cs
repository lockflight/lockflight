using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// What the test suite may not lose between the baseline and a green: no test deleted, newly skipped (<c>Skip = "…"</c> or a runtime
/// <c>Assert.Skip</c>) or newly marked <c>Explicit = true</c>. Explicit tests are read from the runner's listing (<c>"Explicit": true</c>
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
        // NotRun is not counted as a skip: explicit tests are never run in a full run. They are caught from the listing instead.
        HashSet<string> skippedNow = [.. inventory.Where(test => test.SkipReason is not null).Select(test => test.Key),
            .. results.Where(result => result.Outcome == TestOutcome.Skip).Select(result => result.Key)];
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
