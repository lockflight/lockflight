namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>Why a step of an in-session human command was refused: a stable code (the ledger fingerprint's tail) and the reason shown.</summary>
internal sealed record SessionRefusal(string Code, string Reason);
