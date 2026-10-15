namespace Lockflight.Cli.Ledger;

/// <summary>
/// Who made a blocked call and what it was: the tool, the call (a command or a file path, shortened and with long token-like
/// strings masked, because the ledger is readable by the agent), the session and, for a subagent, its type or id.
/// </summary>
internal sealed record LedgerEventContext(string? Tool, string? Call, string? Session, string? Agent);
