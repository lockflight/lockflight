namespace Lockflight.Cli.Ledger;

/// <summary>
/// A review run's summary in the ledger: the run, whether it is of record, how many findings it had and how many blocker/major
/// findings are open (confirmed, unverified) or were fixed after it, and what it used (cost is an estimate at API prices).
/// No finding text: the ledger is readable by the agent and needs only the counts.
/// </summary>
internal sealed record LedgerReview(
    string Feature, string Run, bool OfRecord, int Findings, int OpenConfirmed, int OpenUnverified, int Fixed, long InputTokens, long OutputTokens, decimal EstimatedCostUsd);
