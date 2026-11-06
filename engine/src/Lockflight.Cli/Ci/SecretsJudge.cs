using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The secrets scan of the <c>pr</c> gates, run in the judge's worktree at head before anything there is built: the gitleaks
/// config and <c>.gitleaksignore</c> are the base policy's, laid over the tree, and no code of the pull request has run yet, so
/// a build cannot rewrite them or the gitleaks binary first. The gates later report this result as their <c>security.secrets</c>
/// step. A guardrail while the pull request's code shares the job: what it does after the scan is not covered.
/// </summary>
internal static class SecretsJudge
{
    public static async Task<GateStep> ScanAsync(JudgeWorktree worktree, JudgePlan plan, CliContext context, CancellationToken cancellationToken)
    {
        if (await worktree.MoveToAsync(plan.Inputs.Head, plan.Policy.Commit, clean: false, cancellationToken) is string notMoved)
        {
            return new GateStep(SecretsStep.StepName, 0, Verdict.Error, ReasonCode.ToolError, 0, notMoved);
        }

        GateSettings settings = plan.Policy.Manifest?.Gates ?? GateSettings.Default;
        GateRunContext run = new(worktree.Root, worktree.Root, GateProfile.PullRequest, settings, plan.MergeBase, context.Time)
        {
            GitleaksSha256 = context.Environment.Get(EnvironmentVariables.GitleaksSha256) is { Length: > 0 } sha256 ? sha256 : null,
        };
        return (await new SecretsStep().RunAsync(run, GateRunFacts.None, cancellationToken)).Step;
    }
}
