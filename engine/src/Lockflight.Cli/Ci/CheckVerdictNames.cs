using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Ci;

/// <summary>The contract spellings of <see cref="CheckVerdict"/> and the exit code of a combined verdict.</summary>
internal static class CheckVerdictNames
{
    public static string ToName(CheckVerdict verdict) => verdict switch
    {
        CheckVerdict.Pass => "PASS",
        CheckVerdict.NotEarned => "NOT-EARNED",
        CheckVerdict.Fail => "FAIL",
        CheckVerdict.Error => "ERROR",
        CheckVerdict.NotApplicable => "N/A",
        CheckVerdict.Skipped => "SKIPPED",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unknown verdict."),
    };

    public static CheckVerdict? Parse(string name) => name switch
    {
        "PASS" => CheckVerdict.Pass,
        "NOT-EARNED" => CheckVerdict.NotEarned,
        "FAIL" => CheckVerdict.Fail,
        "ERROR" => CheckVerdict.Error,
        "N/A" => CheckVerdict.NotApplicable,
        "SKIPPED" => CheckVerdict.Skipped,
        _ => null,
    };

    /// <summary>The exit code of a combined verdict (only PASS, FAIL, ERROR or NOT-EARNED come out of a combination).</summary>
    public static ExitCode ToExitCode(CheckVerdict verdict) => verdict switch
    {
        CheckVerdict.Pass => ExitCode.Pass,
        CheckVerdict.Fail => ExitCode.Fail,
        CheckVerdict.Error => ExitCode.Error,
        _ => ExitCode.NotEarned,
    };
}
