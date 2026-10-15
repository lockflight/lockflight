namespace Lockflight.Cli.Gates.Parsers;

/// <summary>What VSTest's TRX reports of one run say together: the counters and the names of the failed results.</summary>
internal sealed record TrxSummary(int Total, int Executed, int Failed, IReadOnlyList<string> FailedTests);
