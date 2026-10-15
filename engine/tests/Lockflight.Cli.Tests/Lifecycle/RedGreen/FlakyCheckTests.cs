using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class FlakyCheckTests
{
    private static readonly TestCase Steady = new("T", "A", "Steady", [], null);
    private static readonly TestCase Wobbly = new("T", "A", "Wobbly", [], null);

    private readonly ManualTime time = new(DateTimeOffset.UnixEpoch);
    private readonly List<string[]> invocations = [];

    [Fact]
    public async Task AllCandidatesAreReRunTogetherAtMostFourTimes()
    {
        FlakyCheck check = new(RunnerPassingAll(), time);

        FlakyCheckResult result = await check.RunAsync([Steady, Wobbly], Deadline(minutes: 10), TestContext.Current.CancellationToken);

        Assert.Empty(result.Flaky);
        Assert.Equal(FlakyCheck.MaxReRuns, result.ReRuns);
        Assert.False(result.BudgetExhausted);
        Assert.All(invocations, keys => Assert.Equal(["T:A.Steady", "T:A.Wobbly"], keys));
    }

    [Fact]
    public async Task ATestThatFailsOnceIsFlakyAndIsNotReRunAgain()
    {
        int call = 0;
        FlakyCheck check = new((tests, _, _) =>
        {
            call++;
            invocations.Add([.. tests.Select(test => test.Key)]);
            ExecutedTest[] results = [.. tests.Select(test => Row(test, test == Wobbly && call == 2 ? TestOutcome.Fail : TestOutcome.Pass))];
            return Task.FromResult<IReadOnlyList<ExecutedTest>?>(results);
        }, time);

        FlakyCheckResult result = await check.RunAsync([Steady, Wobbly], Deadline(minutes: 10), TestContext.Current.CancellationToken);

        Assert.Equal(["T:A.Wobbly"], result.Flaky);
        Assert.Equal(["T:A.Steady"], invocations[^1]);
    }

    [Fact]
    public async Task ReRunsStopWhenTheBudgetRunsOut()
    {
        FlakyCheck check = new((tests, _, _) =>
        {
            invocations.Add([.. tests.Select(test => test.Key)]);
            time.Advance(TimeSpan.FromMinutes(4));
            return Task.FromResult<IReadOnlyList<ExecutedTest>?>([.. tests.Select(test => Row(test, TestOutcome.Pass))]);
        }, time);

        FlakyCheckResult result = await check.RunAsync([Steady], Deadline(minutes: 10), TestContext.Current.CancellationToken);

        Assert.True(result.BudgetExhausted);
        Assert.Equal(3, result.ReRuns);
        Assert.Empty(result.Flaky);
    }

    [Fact]
    public async Task EachReRunGetsOnlyTheTimeLeft()
    {
        List<TimeSpan> limits = [];
        FlakyCheck check = new((tests, limit, _) =>
        {
            limits.Add(limit);
            time.Advance(TimeSpan.FromMinutes(1));
            return Task.FromResult<IReadOnlyList<ExecutedTest>?>([.. tests.Select(test => Row(test, TestOutcome.Pass))]);
        }, time);

        await check.RunAsync([Steady], Deadline(minutes: 10), TestContext.Current.CancellationToken);

        Assert.Equal([10, 9, 8, 7], limits.Select(limit => (int)limit.TotalMinutes));
    }

    [Fact]
    public async Task NoReRunStartsAfterTheDeadline()
    {
        FlakyCheck check = new(RunnerPassingAll(), time);

        FlakyCheckResult result = await check.RunAsync([Steady], DateTimeOffset.UnixEpoch, TestContext.Current.CancellationToken);

        Assert.True(result.BudgetExhausted);
        Assert.Equal(0, result.ReRuns);
        Assert.Empty(invocations);
    }

    [Fact]
    public async Task ARunKilledAtTheDeadlineExhaustsTheBudgetWithoutCallingAnythingFlaky()
    {
        FlakyCheck check = new((_, limit, _) =>
        {
            time.Advance(limit);
            return Task.FromResult<IReadOnlyList<ExecutedTest>?>(null);
        }, time);

        FlakyCheckResult result = await check.RunAsync([Steady], Deadline(minutes: 10), TestContext.Current.CancellationToken);

        Assert.True(result.BudgetExhausted);
        Assert.Empty(result.Flaky);
    }

    [Fact]
    public async Task ARunnerThatBreaksBeforeTheDeadlineMakesTheRemainingTestsFlaky()
    {
        FlakyCheck check = new((_, _, _) => Task.FromResult<IReadOnlyList<ExecutedTest>?>(null), time);

        FlakyCheckResult result = await check.RunAsync([Steady, Wobbly], Deadline(minutes: 10), TestContext.Current.CancellationToken);

        Assert.False(result.BudgetExhausted);
        Assert.Equal(["T:A.Steady", "T:A.Wobbly"], result.Flaky.Order(StringComparer.Ordinal));
    }

    private Func<IReadOnlyList<TestCase>, TimeSpan, CancellationToken, Task<IReadOnlyList<ExecutedTest>?>> RunnerPassingAll() => (tests, _, _) =>
    {
        invocations.Add([.. tests.Select(test => test.Key)]);
        return Task.FromResult<IReadOnlyList<ExecutedTest>?>([.. tests.Select(test => Row(test, TestOutcome.Pass))]);
    };

    private DateTimeOffset Deadline(int minutes) => time.GetUtcNow().AddMinutes(minutes);

    private static ExecutedTest Row(TestCase test, TestOutcome outcome) => new(test.Key, test.MethodFilter, outcome, null, null);
}
