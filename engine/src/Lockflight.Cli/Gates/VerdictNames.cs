using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Gates;

/// <summary>The one place where verdicts map to their contract spellings and exit codes.</summary>
internal static class VerdictNames
{
    /// <summary>The spelling used in CLI output and result files: PASS, NOT-EARNED, FAIL, ERROR, N/A.</summary>
    public static string ToContractName(Verdict verdict) => verdict switch
    {
        Verdict.Pass => "PASS",
        Verdict.NotEarned => "NOT-EARNED",
        Verdict.Fail => "FAIL",
        Verdict.Error => "ERROR",
        Verdict.NotApplicable => "N/A",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unknown verdict."),
    };

    /// <summary>Whether a step lets the run continue: it passed, or it did not apply.</summary>
    public static bool AllowsNextStep(Verdict verdict) => verdict is Verdict.Pass or Verdict.NotApplicable;

    /// <summary>N/A exits 0: nothing applicable was found wanting (only a run in which no step applied ends N/A).</summary>
    public static ExitCode ToExitCode(Verdict verdict) => verdict switch
    {
        Verdict.Pass or Verdict.NotApplicable => ExitCode.Pass,
        Verdict.NotEarned => ExitCode.NotEarned,
        Verdict.Fail => ExitCode.Fail,
        _ => ExitCode.Error,
    };
}
