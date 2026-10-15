namespace Lockflight.Cli.Ledger;

/// <summary>Kinds of facts the Tier-0 ledger records. Capture is deterministic; nothing here calls a model.</summary>
internal enum LedgerEventType
{
    /// <summary>A hook blocked an agent action (protected path, invalid manifest, stale engine, red stop gate).</summary>
    HookBlocked = 1,

    /// <summary>A gate run finished with a verdict.</summary>
    GateCompleted = 2,

    /// <summary>A sanctioned Lockflight command (init, baseline, adopt, update, workspace sync) wrote a protected file.</summary>
    ConfigWritten = 3,

    /// <summary>A lifecycle step produced evidence: a red/green cycle accepted, rejected or closed, a review recorded.</summary>
    EvidenceRecorded = 4,

    /// <summary>A person approved a feature's spec or tasks.</summary>
    ApprovalRecorded = 5,
}
