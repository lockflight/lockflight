namespace Lockflight.Cli.Init;

/// <summary>What a GitHub workflow is to <c>update</c>'s refresh of the CI judge's workflow (<see cref="JudgeWorkflowRefresh"/>).</summary>
internal enum JudgeWorkflowRefreshStatus
{
    /// <summary>Not the CI judge's workflow (a gates-only one, or none Lockflight wrote): the refresh leaves it to the other migrations.</summary>
    NotJudge = 0,

    /// <summary>Exactly what this engine writes for its channels, trigger, runner and workspace shell: nothing to change.</summary>
    Current = 1,

    /// <summary>A template of this or an earlier release, as Lockflight wrote it: the replacement is this engine's.</summary>
    Outdated = 2,

    /// <summary>Edited outside what Lockflight writes: a person merges the replacement.</summary>
    Edited = 3,
}
