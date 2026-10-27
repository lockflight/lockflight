using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class TimeBudgetTests
{
    [Fact]
    public void EachProcessGetsOnlyWhatTheOnesBeforeItLeft()
    {
        ManualTime time = new(DateTimeOffset.UnixEpoch);
        TimeBudget budget = new(TimeSpan.FromMinutes(5), time);

        time.Advance(TimeSpan.FromMinutes(3));
        TimeSpan second = budget.Left;
        time.Advance(TimeSpan.FromMinutes(4));

        Assert.Equal(TimeSpan.FromMinutes(2), second);
        Assert.True(budget.IsSpent);
        Assert.Equal(TimeSpan.Zero, budget.Left);
    }

    [Fact]
    public void WithoutALimitTheBudgetIsNeverSpent()
    {
        ManualTime time = new(DateTimeOffset.UnixEpoch);
        TimeBudget budget = new(Timeout.InfiniteTimeSpan, time);

        time.Advance(TimeSpan.FromDays(1));

        Assert.Equal(Timeout.InfiniteTimeSpan, budget.Left);
        Assert.False(budget.IsSpent);
    }
}
