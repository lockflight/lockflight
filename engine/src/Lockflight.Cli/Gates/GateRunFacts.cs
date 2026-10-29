namespace Lockflight.Cli.Gates;

/// <summary>
/// What earlier steps of one run (in a workspace, of one unit) reported, for the steps that need it: the test count for the
/// result, and where and when the tests ran for the coverage step. A step that needs a new fact adds a field here.
/// </summary>
/// <param name="TestsTotal">The tests the test step counted; null before it ran or when the runner reported no count.</param>
/// <param name="TestResultsDirectory">Where a VSTest run wrote its results (Cobertura in per-run folders); null on MTP.</param>
/// <param name="TestsStarted">When the test run started: coverage reports older than that are not this run's.</param>
internal sealed record GateRunFacts(int? TestsTotal, string? TestResultsDirectory, DateTimeOffset? TestsStarted)
{
    public static GateRunFacts None { get; } = new(null, null, null);
}
