namespace Lockflight.Cli.Ci;

/// <summary>A finished judge run: its plan, what building and running found, the result of every check, and when it started and finished.</summary>
internal sealed record JudgeRun(JudgePlan Plan, JudgeFindings Findings, IReadOnlyList<CheckResult> Checks, DateTimeOffset StartedAt, DateTimeOffset FinishedAt)
{
    public CheckVerdict Verdict => JudgeVerdicts.Combine(Checks);

    public CheckVerdict VerdictOf(StatusContext context) => JudgeVerdicts.Of(context, Checks);
}
