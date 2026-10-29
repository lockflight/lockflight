using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Ci;

/// <summary>
/// One red commit as the judge re-ran it: how its tree built, the new tests with their classes and why (empty when none could be
/// run), and the cycle's verdict with the line that explains it.
/// </summary>
internal sealed record CycleJudgement(RedCommit Red, TreeOutcome Build, IReadOnlyList<RedGreenTest> Tests, IReadOnlyDictionary<string, RedVerdict> Verdicts, CheckVerdict Verdict, string Why)
{
    /// <summary>The new tests' fingerprints at the red commit, by key; a test without one counts as changed since.</summary>
    public IReadOnlyDictionary<string, string> FingerprintsAtRed { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The host-tampering findings in the red commit's test assemblies.</summary>
    public IReadOnlyList<TamperingFinding> Tampering { get; init; } = [];

    public bool Accepted => Verdict == CheckVerdict.Pass;
}
