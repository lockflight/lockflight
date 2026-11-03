namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// The trace rows counted the same way locally and in CI: requirements covered by .NET tests, requirements verified outside .NET
/// (and not covered), and the IDs of the rest.
/// </summary>
internal sealed record TraceTally(int Covered, int Excluded, IReadOnlyList<string> Uncovered)
{
    public TraceOutcome Outcome =>
        Uncovered.Count > 0 ? TraceOutcome.Fail
        : Covered == 0 && Excluded > 0 ? TraceOutcome.NotApplicable
        : TraceOutcome.Pass;

    public static TraceTally Of(IReadOnlyList<TraceRow> rows) =>
        new(rows.Count(row => row.Covered), rows.Count(row => row.Excluded), [.. rows.Where(row => !row.Covered && !row.Excluded).Select(row => row.RequirementId)]);
}
