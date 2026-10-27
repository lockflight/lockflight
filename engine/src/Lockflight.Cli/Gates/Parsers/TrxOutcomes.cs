namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// The TRX outcomes that mean a test or a run did not complete: it timed out, was aborted, errored or lost its test host. They are
/// neither a pass nor "not run", so no reader may skip them or let a baseline cover them.
/// </summary>
internal static class TrxOutcomes
{
    private static readonly HashSet<string> Abnormal = new(StringComparer.Ordinal) { "Error", "Timeout", "Aborted", "Disconnected", "PassedButRunAborted" };

    /// <summary>The outcome (of a <c>UnitTestResult</c> or of a <c>ResultSummary</c>) says the test or run did not complete.</summary>
    public static bool IsAbnormal(string? outcome) => outcome is not null && Abnormal.Contains(outcome);
}
