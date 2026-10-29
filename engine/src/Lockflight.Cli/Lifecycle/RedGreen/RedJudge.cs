using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// What a first red and the red after an amend judge alike: whether the suite lost or weakened tests, how each test failed and why
/// (shown under its line; <paramref name="verbose"/> adds the exception, the deciding frame and the production call), and the warning
/// for tests that assert nothing.
/// </summary>
internal sealed class RedJudge(RedGreenSession session, bool verbose)
{
    /// <summary>Indents the why lines under the test's name, past the class column.</summary>
    private const string WhyIndent = "             ";

    /// <summary>Whether the suite lost or weakened tests since the baseline and earlier cycles; says why when it did.</summary>
    public async Task<bool> SuiteProblemAsync(RedGreenRecord record, IReadOnlyList<TestCase> inventory, CancellationToken cancellationToken)
    {
        HashSet<string> current = [.. inventory.Select(test => test.Key)];
        string[] deleted = [.. record.KnownTests.Where(key => !current.Contains(key)).Order(StringComparer.Ordinal)];
        if (deleted.Length > 0)
        {
            await session.FailAsync($"tests disappeared since the baseline: {string.Join(", ", deleted.Take(RedGreenSession.MaxTestsListed))}; restore them (deleting tests is not part of a red)", cancellationToken);
            return true;
        }

        if (SuiteChecks.WeakenedKnownTests(record, inventory) is { Count: > 0 } weakened)
        {
            await session.FailAsync($"{string.Join("; ", weakened)}; restore them (a red adds tests, it does not switch existing ones off)", cancellationToken);
            return true;
        }

        return false;
    }

    public async Task WarnAssertionFreeAsync(List<RedGreenTest> classified)
    {
        string[] assertionFree = [.. classified.Where(test => test.AssertionFree).Select(test => TraceMatrix.ShortName(test.MethodFilter))];
        if (assertionFree.Length > 0)
        {
            await session.Output.WriteLineAsync(
                $"Warning: {assertionFree.Length} new tests assert nothing ({RedGreenTest.AssertionFreeMark}): {string.Join(", ", assertionFree.Take(RedGreenSession.MaxTestsListed))}. " +
                "Such a test passes as soon as the code returns without throwing. Add an assertion on the result; " +
                "a test that verifies by letting the production code throw may keep this warning, which trace and review show.");
        }
    }

    /// <summary>Why the test got its class, under its line; with <c>--verbose</c> also the exception, the deciding frame and the production call.</summary>
    public async Task WriteWhyAsync(RedVerdict verdict)
    {
        await session.Output.WriteLineAsync($"{WhyIndent}{verdict.Why}");
        if (verbose)
        {
            await session.Output.WriteLineAsync($"{WhyIndent}{verdict.Detail}");
        }
    }

    /// <summary>
    /// Classifies each new test by how it failed, with why; tests that passed every time they ran are re-run together to tell flaky
    /// from green (<see cref="RedEvaluation"/>).
    /// </summary>
    public async Task<(List<RedGreenTest> Classified, FlakyCheckResult FlakyCheck, IReadOnlyDictionary<string, RedVerdict> Verdicts)> ClassifyAsync(
        TestWorkspace workspace,
        string root,
        IReadOnlyList<TestCase> inventory,
        TestCase[] fresh,
        IReadOnlyList<ExecutedTest> results,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        RedEvaluationResult evaluation = await RedEvaluation.ClassifyAsync(workspace, root, inventory, fresh, results, deadline, session.Context.Time, cancellationToken);
        return (evaluation.Classified, evaluation.FlakyCheck, evaluation.Verdicts);
    }
}
