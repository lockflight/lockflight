namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>The lifecycle points where a person approves the feature's artifacts.</summary>
internal enum ApprovalPhase
{
    /// <summary>The specification (spec.md) is approved before planning.</summary>
    Spec = 1,

    /// <summary>The task list (tasks.md, with plan.md when present) is approved before implementation.</summary>
    Tasks = 2,
}
