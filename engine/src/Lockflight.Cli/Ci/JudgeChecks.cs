namespace Lockflight.Cli.Ci;

/// <summary>
/// The checks P0 can already decide from git alone, and the honest placeholder for the ones that need a build: NOT-EARNED, never a
/// guess. An off check is N/A.
/// </summary>
internal static class JudgeChecks
{
    public const string NotJudgedYet = "not judged by this engine version yet";

    public static IReadOnlyList<CheckResult> FromPlan(JudgePlan plan) =>
        [.. Enum.GetValues<CheckId>().Select(check => Decide(plan, check, plan.Levels[check]))];

    private static CheckResult Decide(JudgePlan plan, CheckId check, CheckLevel level)
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
            CheckId.Review => new CheckResult(check, level, CheckVerdict.Skipped, plan.Inputs.ReviewExpected ? "the report joins the review job's result" : "no review job runs for this pull request"),
            _ => new CheckResult(check, level, CheckVerdict.NotEarned, NotJudgedYet),
        };
    }

    private static string NoRedWhy(JudgePlan plan)
    {
        int malformed = plan.Reds.All.Count(red => red.Malformed is not null);
        string trailers = malformed > 0 ? $" ({malformed} commits carry a malformed Lockflight-Red trailer)" : string.Empty;
        return $"no red commit in this pull request{trailers}; commit each story's failing tests and stubs with a 'Lockflight-Red: <cycle>' trailer before the implementation";
    }
}
