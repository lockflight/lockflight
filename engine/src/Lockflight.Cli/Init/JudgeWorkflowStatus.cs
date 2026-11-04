namespace Lockflight.Cli.Init;

/// <summary>What a GitHub workflow is to the move from the gates alone to the CI judge's workflow.</summary>
internal enum JudgeWorkflowStatus
{
    /// <summary>Not a workflow that runs the gates alone: the CI judge's already, or not one Lockflight wrote.</summary>
    NotGatesOnly = 0,

    /// <summary>A gates-only workflow exactly as Lockflight 0.2 wrote it (apart from the lines Lockflight itself moves): it can be replaced.</summary>
    Recognised = 1,

    /// <summary>A gates-only workflow with edits outside the lines Lockflight wrote: a person moves it.</summary>
    Edited = 2,
}
