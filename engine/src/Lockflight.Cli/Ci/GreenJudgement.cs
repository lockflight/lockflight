namespace Lockflight.Cli.Ci;

/// <summary>
/// Green at head: the verdict and its line, how many test cases ran and failed, the problems found, the tests that passed only on
/// a re-run, where each cycle test stands at head (<c>pass</c>, <c>fail</c>, <c>missing</c>, <c>skipped</c>, <c>explicit</c>,
/// <c>flaky</c>), and the tests new at head that no red commit introduced.
/// </summary>
internal sealed record GreenJudgement(
    CheckVerdict Verdict,
    string Why,
    int Total,
    int Failed,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Flaky,
    IReadOnlyDictionary<string, string> AtHead,
    IReadOnlyList<string> WithoutRed);
