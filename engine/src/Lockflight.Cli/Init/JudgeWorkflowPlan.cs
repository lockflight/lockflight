namespace Lockflight.Cli.Init;

/// <summary>A gates-only GitHub workflow's move to the CI judge's workflow: whether it can be made, and the new text when it can.</summary>
internal sealed record JudgeWorkflowPlan(JudgeWorkflowStatus Status, string? Replacement)
{
    public static JudgeWorkflowPlan NotGatesOnly { get; } = new(JudgeWorkflowStatus.NotGatesOnly, null);

    public static JudgeWorkflowPlan Edited { get; } = new(JudgeWorkflowStatus.Edited, null);
}
