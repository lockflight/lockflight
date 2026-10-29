namespace Lockflight.Cli.Ci;

/// <summary>
/// Combines check results into one verdict: any required FAIL fails; else any required ERROR is an error; else any required check
/// that is NOT-EARNED, SKIPPED or N/A is NOT-EARNED; else PASS. Advisory and off checks never change the verdict.
/// </summary>
internal static class JudgeVerdicts
{
    public static CheckVerdict Combine(IEnumerable<CheckResult> checks)
    {
        CheckVerdict[] required = [.. checks.Where(check => check.Level == CheckLevel.Required).Select(check => check.Verdict)];
        return required.Contains(CheckVerdict.Fail) ? CheckVerdict.Fail
            : required.Contains(CheckVerdict.Error) ? CheckVerdict.Error
            : required.Any(verdict => verdict != CheckVerdict.Pass) ? CheckVerdict.NotEarned
            : CheckVerdict.Pass;
    }

    /// <summary>The verdict of one status context: its own checks only.</summary>
    public static CheckVerdict Of(StatusContext context, IEnumerable<CheckResult> checks) => Combine(checks.Where(check => check.Context == context));
}
