using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// Builds the trace rows from the feature's red/green record, re-checked against the current test inventory: a test that went red,
/// then green counts for a requirement only while it still exists under the same name, still names the requirement in its
/// <c>Requirement</c> trait, and is neither skipped nor explicit. A test deleted or renamed after its green would otherwise stay in
/// the matrix forever. A test that asserts nothing still counts, marked assertion-free, so the review sees it.
/// </summary>
internal static class TraceMatrix
{
    public static IReadOnlyList<TraceRow> Build(IReadOnlyList<Requirement> requirements, RedGreenRecord record, IReadOnlyList<TestCase> inventory)
    {
        Dictionary<string, TestCase> current = inventory.DistinctBy(test => test.Key).ToDictionary(test => test.Key, StringComparer.Ordinal);
        RedGreenTest[] green = [.. record.Cycles.Where(cycle => !cycle.IsOpen).SelectMany(cycle => cycle.Tests)];
        RedGreenTest[] red = [.. record.Cycles.Where(cycle => cycle.IsOpen).SelectMany(cycle => cycle.Tests)];
        List<TraceRow> rows = [];
        foreach (Requirement requirement in requirements)
        {
            List<string> tests = [];
            List<string> lost = [];
            foreach (RedGreenTest test in green.Where(test => test.Requirements.Contains(requirement.Id)))
            {
                if (LostReason(test, requirement.Id, current) is string reason)
                {
                    lost.Add($"{ShortName(test.MethodFilter)} ({reason})");
                }
                else
                {
                    tests.Add(test.AssertionFree ? $"{ShortName(test.MethodFilter)} ({RedGreenTest.AssertionFreeMark})" : ShortName(test.MethodFilter));
                }
            }

            rows.Add(new TraceRow(requirement.Id, tests, lost, red.Any(test => test.Requirements.Contains(requirement.Id))));
        }

        return rows;
    }

    public static string ShortName(string methodFilter)
    {
        string[] parts = methodFilter.Split('.');
        return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : methodFilter;
    }

    private static string? LostReason(RedGreenTest test, string requirementId, Dictionary<string, TestCase> current) =>
        !current.TryGetValue(test.Key, out TestCase? now) ? "deleted or renamed"
        : !now.Requirements.Contains(requirementId) ? $"no longer names {requirementId}"
        : now.SkipReason is not null ? "skipped"
        : now.IsExplicit ? "explicit"
        : null;
}
