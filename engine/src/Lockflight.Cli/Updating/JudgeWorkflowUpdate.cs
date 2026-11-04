namespace Lockflight.Cli.Updating;

/// <summary>What <c>update</c> does with the GitHub workflow in the move from the gates alone to the CI judge's workflow.</summary>
internal enum JudgeWorkflowUpdate
{
    /// <summary>Nothing planned for it here: the CI step migration may still move it.</summary>
    Unchanged = 0,

    /// <summary>A write of the workflow is planned (the CI judge's workflow, or a 0.3.0 judge workflow renamed); nothing else may write it.</summary>
    Planned = 1,

    /// <summary>The replacement was asked for and cannot be made; the update stops before it writes anything.</summary>
    Refused = 2,
}
