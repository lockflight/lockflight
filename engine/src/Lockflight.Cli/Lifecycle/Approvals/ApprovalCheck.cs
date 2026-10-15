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
            ApprovalState.Stale => $"{phase} approval is stale: {string.Join(", ", ChangedArtifacts)} changed; ask the user to run 'lockflight approve {phase}' again in their own terminal",
            ApprovalState.Invalid => $"{phase} approval record is not valid here: {Problem}; ask the user to run 'lockflight approve {phase}' in their own terminal",
            _ => $"{phase} is not approved; ask the user to review it and run 'lockflight approve {phase}' in their own terminal",
        };
    }
}
