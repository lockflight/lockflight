namespace Lockflight.Cli.Ledger;

/// <summary>Contract spellings of <see cref="LedgerEventType"/> in the JSONL ledger.</summary>
internal static class LedgerEventTypeNames
{
    public const string HookBlocked = "hook.blocked";
    public const string GateCompleted = "gate.completed";
    public const string ConfigWritten = "config.written";
    public const string EvidenceRecorded = "evidence.recorded";
    public const string ApprovalRecorded = "approval.recorded";
    public const string ProposalCreated = "proposal.created";
    public const string ReviewCompleted = "review.completed";
    public const string FindingDismissed = "finding.dismissed";
    public const string FindingFixed = "finding.fixed";
    public const string SessionCommandAnswered = "session-command.answered";

    public static string ToName(LedgerEventType type) => type switch
    {
        LedgerEventType.HookBlocked => HookBlocked,
        LedgerEventType.GateCompleted => GateCompleted,
        LedgerEventType.ConfigWritten => ConfigWritten,
        LedgerEventType.EvidenceRecorded => EvidenceRecorded,
        LedgerEventType.ApprovalRecorded => ApprovalRecorded,
        LedgerEventType.ProposalCreated => ProposalCreated,
        LedgerEventType.ReviewCompleted => ReviewCompleted,
        LedgerEventType.FindingDismissed => FindingDismissed,
        LedgerEventType.FindingFixed => FindingFixed,
        LedgerEventType.SessionCommandAnswered => SessionCommandAnswered,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown ledger event type."),
    };
}
