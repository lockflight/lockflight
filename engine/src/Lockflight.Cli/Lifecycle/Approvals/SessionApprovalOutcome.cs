namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>What the <c>UserPromptExpansion</c> hook does with a typed slash command.</summary>
internal enum SessionApprovalOutcome
{
    /// <summary>Not <c>/lockflight:approve</c>: the expansion goes ahead untouched.</summary>
    Ignored = 0,

    /// <summary>The approval is refused; the person sees why and the command for a separate terminal, the agent sees nothing.</summary>
    Refused = 1,

    /// <summary>The digest and a one-time code are shown to the person only; nothing is recorded yet.</summary>
    Shown = 2,

    /// <summary>The approval is recorded; the person gets a confirmation and the agent a note to re-read the approval state.</summary>
    Recorded = 3,
}
