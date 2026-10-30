namespace Lockflight.Cli.Ledger;

/// <summary>Kinds of facts the Tier-0 ledger records. Capture is deterministic; nothing here calls a model.</summary>
internal enum LedgerEventType
{
    /// <summary>A hook blocked an agent action (protected path, invalid manifest, stale engine, red stop gate).</summary>
    HookBlocked = 1,

    /// <summary>A gate run finished with a verdict.</summary>
    GateCompleted = 2,

    /// <summary>A sanctioned Lockflight command (init, baseline, update, workspace sync) wrote a protected file.</summary>
    ConfigWritten = 3,

    /// <summary>A lifecycle step produced evidence: a red/green cycle accepted, rejected or closed (reviews before review.completed existed).</summary>
    EvidenceRecorded = 4,

    /// <summary>A person approved a feature's spec or tasks.</summary>
    ApprovalRecorded = 5,

    /// <summary><c>lockflight propose</c> wrote a patch for a person to apply (the path and its rule, never the content).</summary>
    ProposalCreated = 6,

    /// <summary>A review run finished: whether it is of record, how many findings are open, what it used (no finding text).</summary>
    ReviewCompleted = 7,

    /// <summary>A person dismissed an open review finding (its id, never the reason or the person).</summary>
    FindingDismissed = 8,

    /// <summary>A review re-ran an open finding's reproduction and it passed.</summary>
    FindingFixed = 9,

    /// <summary>
    /// An in-session human command was answered without recording anything: a usage hint, a wrong, expired or used code, or
    /// changed content. The person's own typing, not a block of the agent, so neither the status line nor the crew's retro counts it.
    /// </summary>
    SessionCommandAnswered = 10,
}
