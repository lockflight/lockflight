namespace Lockflight.Cli.Init;

/// <summary>
/// The refresh of a CI judge's workflow: what the file is, and what this engine would write in its place (null for
/// <see cref="JudgeWorkflowRefreshStatus.NotJudge"/>). For an edited file the replacement is only shown, never written.
/// </summary>
internal sealed record JudgeWorkflowRefreshPlan(JudgeWorkflowRefreshStatus Status, string? Replacement)
{
    public static JudgeWorkflowRefreshPlan NotJudge { get; } = new(JudgeWorkflowRefreshStatus.NotJudge, null);
}
