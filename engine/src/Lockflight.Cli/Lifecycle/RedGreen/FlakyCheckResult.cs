namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// The outcome of re-running the new tests that passed on a red: the keys that failed at least once (FLAKY), how many re-runs were
/// made, and whether the time budget ran out before every re-run was made (the remaining tests then stay GREEN_BEFORE).
/// </summary>
internal sealed record FlakyCheckResult(IReadOnlySet<string> Flaky, int ReRuns, bool BudgetExhausted);
