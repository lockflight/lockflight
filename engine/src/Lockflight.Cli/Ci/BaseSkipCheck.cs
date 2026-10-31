namespace Lockflight.Cli.Ci;

/// <summary>
/// The base tests head skipped at run time, run again on the base (<see cref="GreenJudge.SkippedAtRunTime"/>): per test, how many
/// of head's skipped rows the base skipped too with the same skip message, and, when the base could not run them, why. Those rows
/// are not new skips; a comparison that could not be made never fails the check, it leaves it NOT-EARNED.
/// </summary>
/// <param name="MatchedRows">Per test key, the head's skipped rows that a base row skipped with the same message.</param>
/// <param name="Unchecked">The tests the base could not run (empty when it ran them).</param>
/// <param name="Failure">Why the base could not run them; null when it did or there was nothing to run.</param>
internal sealed record BaseSkipCheck(IReadOnlyDictionary<string, int> MatchedRows, IReadOnlyList<string> Unchecked, string? Failure)
{
    /// <summary>Nothing to compare: no base test was skipped at run time at head.</summary>
    public static BaseSkipCheck None { get; } = new(new Dictionary<string, int>(StringComparer.Ordinal), [], null);
}
