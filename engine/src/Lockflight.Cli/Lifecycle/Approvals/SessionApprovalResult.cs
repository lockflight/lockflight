namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// The hook's answer to <c>/lockflight:approve</c>: the outcome, a stable <see cref="Code"/> for a refusal (the ledger fingerprint),
/// the message for the person and, once recorded, the note for the agent.
/// </summary>
internal sealed record SessionApprovalResult(SessionApprovalOutcome Outcome, string? Code, string? Message, string? AgentContext)
{
    public static SessionApprovalResult Ignored { get; } = new(SessionApprovalOutcome.Ignored, null, null, null);

    public static SessionApprovalResult Refused(string code, string message) => new(SessionApprovalOutcome.Refused, code, message, null);

    public static SessionApprovalResult Shown(string digest) => new(SessionApprovalOutcome.Shown, null, digest, null);

    public static SessionApprovalResult Recorded(string message, string agentContext) => new(SessionApprovalOutcome.Recorded, null, message, agentContext);
}
