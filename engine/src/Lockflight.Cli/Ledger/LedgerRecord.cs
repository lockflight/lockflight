namespace Lockflight.Cli.Ledger;

/// <summary>
/// One ledger line as read back: type and signature in their contract spellings. The gate run fields are null on lines written
/// before they were recorded and on events that are not gate runs.
/// </summary>
internal sealed record LedgerRecord(DateTimeOffset At, string Type, string Signature, string Summary, string? Detail)
{
    /// <summary>A <see cref="GateTriggerNames"/> spelling.</summary>
    public string? Trigger { get; init; }

    public long? DurationMs { get; init; }

    /// <summary>A <see cref="StopCacheResultNames"/> spelling.</summary>
    public string? Cache { get; init; }
}
