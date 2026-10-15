namespace Lockflight.Cli.Ledger;

/// <summary>One ledger line as read back: type and signature in their contract spellings.</summary>
internal sealed record LedgerRecord(DateTimeOffset At, string Type, string Signature, string Summary, string? Detail);
