namespace Lockflight.Cli.Ledger;

/// <summary>
/// How a gate run came about and what it cost: its trigger, its wall-clock time in milliseconds (for a cache hit, the time to
/// compute the inputs hash) and whether the Stop cache answered it.
/// </summary>
internal sealed record LedgerGateRun(GateTrigger Trigger, long DurationMs, StopCacheResult Cache);
