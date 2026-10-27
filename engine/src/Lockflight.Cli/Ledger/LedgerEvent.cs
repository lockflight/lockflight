namespace Lockflight.Cli.Ledger;

/// <summary>
/// One ledger line. <see cref="Signature"/> is the fingerprint recurring incidents are grouped by: stable across occurrences,
/// free of timestamps and absolute paths. A hook block also carries who did what (<see cref="Context"/>), so a retro can tell
/// the main session from a subagent and see the blocked call; a gate run carries its trigger and cost (<see cref="GateRun"/>);
/// an approval carries what it bound (<see cref="Approval"/>); a review run its summary (<see cref="Review"/>); a finding event the
/// finding's id (<see cref="Finding"/>). Each is written as extra fields, so older readers ignore them.
/// </summary>
internal sealed record LedgerEvent(LedgerEventType Type, string Signature, string Summary, string? Detail)
{
    public LedgerEventContext? Context { get; init; }

    public LedgerGateRun? GateRun { get; init; }

    public LedgerApproval? Approval { get; init; }

    public LedgerReview? Review { get; init; }

    public LedgerFinding? Finding { get; init; }
}
