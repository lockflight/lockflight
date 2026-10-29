namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>A red's new tests as classified, the FLAKY re-check's outcome, and each test's verdict with why, by test key.</summary>
internal sealed record RedEvaluationResult(List<RedGreenTest> Classified, FlakyCheckResult FlakyCheck, IReadOnlyDictionary<string, RedVerdict> Verdicts);
