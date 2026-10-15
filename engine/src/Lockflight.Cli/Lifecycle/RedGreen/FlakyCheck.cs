using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// Tells FLAKY from GREEN_BEFORE for new tests that passed on a red. The re-runs are bounded twice: at most <see cref="MaxReRuns"/>
/// runner invocations per red, each covering every test still unproven (not one invocation per test), and all of them inside the
/// red's time budget (<c>gates.timeoutSeconds</c>, counted from the start of the red). When the budget runs out the check stops and the
/// unproven tests keep GREEN_BEFORE. That verdict stays honest: a test that passed before the implementation existed rejects the red
/// whether or not it is also flaky — the re-runs only choose the label, never the outcome.
/// </summary>
internal sealed class FlakyCheck(Func<IReadOnlyList<TestCase>, TimeSpan, CancellationToken, Task<IReadOnlyList<ExecutedTest>?>> runTests, TimeProvider time)
{
    /// <summary>Re-runs after the first run, so a test is seen five times in all before it is called GREEN_BEFORE.</summary>
    public const int MaxReRuns = 4;

    public async Task<FlakyCheckResult> RunAsync(IReadOnlyList<TestCase> candidates, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        HashSet<string> flaky = new(StringComparer.Ordinal);
        List<TestCase> unproven = [.. candidates];
        int reRuns = 0;
        while (unproven.Count > 0 && reRuns < MaxReRuns)
        {
            TimeSpan left = deadline - time.GetUtcNow();
            if (left <= TimeSpan.Zero)
            {
                return new FlakyCheckResult(flaky, reRuns, BudgetExhausted: true);
            }

            IReadOnlyList<ExecutedTest>? results = await runTests(unproven, left, cancellationToken);
            reRuns++;
            if (results is null)
            {
                // No report: either the run was killed at the budget, or the runner itself broke while re-running tests that had
                // passed — the latter is instability, as before re-runs were batched.
                if (time.GetUtcNow() >= deadline)
                {
                    return new FlakyCheckResult(flaky, reRuns, BudgetExhausted: true);
                }

                flaky.UnionWith(unproven.Select(test => test.Key));
                break;
            }

            foreach (TestCase test in unproven.Where(test => !SuiteChecks.PassedEveryRow(results, test.Key)).ToArray())
            {
                flaky.Add(test.Key);
                unproven.Remove(test);
            }
        }

        return new FlakyCheckResult(flaky, reRuns, BudgetExhausted: false);
    }
}
