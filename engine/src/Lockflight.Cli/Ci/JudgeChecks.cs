namespace Lockflight.Cli.Ci;

/// <summary>
/// Each check's verdict from the plan and what building and running found; a check this engine does not judge yet is NOT-EARNED,
/// never a guess. An off check is N/A.
/// </summary>
internal static class JudgeChecks
{
    public const string NotJudgedYet = "not judged by this engine version yet";

    public static IReadOnlyList<CheckResult> Decide(JudgePlan plan, JudgeFindings findings) =>
        [.. Enum.GetValues<CheckId>().Select(check => Decide(plan, findings, check, plan.Levels[check]))];

    private static CheckResult Decide(JudgePlan plan, JudgeFindings findings, CheckId check, CheckLevel level)
    {
        if (level == CheckLevel.Off)
        {
            return new CheckResult(check, level, CheckVerdict.NotApplicable, plan.Inputs.Event == JudgeEvent.PullRequest ? "off for this change class" : "judged on the pull request");
        }

        return check switch
        {
            CheckId.Red when plan.Reds.OverCap => new CheckResult(check, level, CheckVerdict.NotEarned,
                $"{plan.Reds.Cycles.Count} red commits, more than ci.maxRedCommits ({plan.Policy.Ci.MaxRedCommits}); split the pull request"),
            CheckId.Red when plan.Reds.Cycles.Count == 0 => new CheckResult(check, level, CheckVerdict.NotEarned, NoRedWhy(plan)),
            CheckId.Red when findings.Cycles is IReadOnlyList<CycleJudgement> cycles => Red(level, cycles),
            CheckId.Review => new CheckResult(check, level, CheckVerdict.Skipped, plan.Inputs.ReviewExpected ? "the report joins the review job's result" : "no review job runs for this pull request"),
            _ => new CheckResult(check, level, CheckVerdict.NotEarned, NotJudgedYet),
        };
    }

    /// <summary>Every cycle accepted passes; otherwise the worst cycle decides, and its line says why.</summary>
    private static CheckResult Red(CheckLevel level, IReadOnlyList<CycleJudgement> cycles)
    {
        CycleJudgement? worst = cycles.FirstOrDefault(cycle => cycle.Verdict == CheckVerdict.Fail)
            ?? cycles.FirstOrDefault(cycle => cycle.Verdict == CheckVerdict.Error)
            ?? cycles.FirstOrDefault(cycle => cycle.Verdict != CheckVerdict.Pass);
        return worst is null
            ? new CheckResult(CheckId.Red, level, CheckVerdict.Pass, $"{cycles.Count} cycles, {cycles.Sum(cycle => cycle.Tests.Count)} tests, each red in production code")
            : new CheckResult(CheckId.Red, level, worst.Verdict, $"cycle {worst.Red.Label} ({CommitIds.Short(worst.Red.Commit)}): {worst.Why}");
    }

    private static string NoRedWhy(JudgePlan plan)
    {
        int malformed = plan.Reds.All.Count(red => red.Malformed is not null);
        string trailers = malformed > 0 ? $" ({malformed} commits carry a malformed Lockflight-Red trailer)" : string.Empty;
        return $"no red commit in this pull request{trailers}; commit each story's failing tests and stubs with a 'Lockflight-Red: <cycle>' trailer before the implementation";
    }
}
