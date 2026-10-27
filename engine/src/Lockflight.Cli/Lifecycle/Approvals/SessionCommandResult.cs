namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// The hook's answer to an in-session human command: the outcome, a stable <see cref="Code"/> for a refusal (the ledger
/// fingerprint), the message for the person and, once recorded, the note for the agent.
/// </summary>
internal sealed record SessionCommandResult(SessionCommandOutcome Outcome, string? Code, string? Message, string? AgentContext)
{
    public static SessionCommandResult Ignored { get; } = new(SessionCommandOutcome.Ignored, null, null, null);

    public static SessionCommandResult Refused(string code, string message) => new(SessionCommandOutcome.Refused, code, message, null);

    public static SessionCommandResult Answered(string code, string message) => new(SessionCommandOutcome.Answered, code, message, null);

    public static SessionCommandResult Shown(string digest) => new(SessionCommandOutcome.Shown, null, digest, null);

    public static SessionCommandResult Recorded(string message, string agentContext) => new(SessionCommandOutcome.Recorded, null, message, agentContext);
}
