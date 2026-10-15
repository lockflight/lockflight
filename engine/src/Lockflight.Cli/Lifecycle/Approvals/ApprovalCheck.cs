using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// The state of one phase's approval and, when stale, the artifacts that changed since; when invalid, <see cref="Problem"/> says why.
/// </summary>
internal sealed record ApprovalCheck(ApprovalPhase Phase, ApprovalState State, string? ApprovedBy, IReadOnlyList<string> ChangedArtifacts, string? Problem = null)
{
    public string Describe()
    {
        string phase = ApprovalPhaseNames.ToName(Phase);
        return State switch
        {
            ApprovalState.Valid => $"{phase} approved by {ApprovedBy}",
            ApprovalState.Stale => $"{phase} approval is stale: {string.Join(", ", ChangedArtifacts)} changed; {HumanOnlyCommands.AskTheUser($"approve {phase}")} again",
            ApprovalState.Invalid => $"{phase} approval record is not valid here: {Problem}; {HumanOnlyCommands.AskTheUser($"approve {phase}")}",
            _ => $"{phase} is not approved; ask the user to review it, then {HumanOnlyCommands.AskTheUser($"approve {phase}")[4..]}",
        };
    }
}
