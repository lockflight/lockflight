namespace Lockflight.Cli.Ledger;

/// <summary>
/// One ledger line. <see cref="Signature"/> is the fingerprint recurring incidents are grouped by: stable across occurrences,
/// free of timestamps and absolute paths. A hook block also carries who did what (<see cref="Context"/>), so a retro can tell
/// the main session from a subagent and see the blocked call.
/// </summary>
internal sealed record LedgerEvent(LedgerEventType Type, string Signature, string Summary, string? Detail)
{
    public LedgerEventContext? Context { get; init; }
}
