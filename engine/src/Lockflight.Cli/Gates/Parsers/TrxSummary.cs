namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// What VSTest's TRX reports of one run say together: the counters and the names of the failed results. <see cref="Incomplete"/>
/// names each report whose run did not complete (a crashed test host, an aborted run) with the reason, and
/// <see cref="AbnormalTests"/> each result that timed out, was aborted or errored, with its outcome.
/// </summary>
internal sealed record TrxSummary(int Total, int Executed, int Failed, IReadOnlyList<string> FailedTests)
{
    public IReadOnlyList<string> Incomplete { get; init; } = [];

    public IReadOnlyList<string> AbnormalTests { get; init; } = [];
}
