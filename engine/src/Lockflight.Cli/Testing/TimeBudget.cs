namespace Lockflight.Cli.Testing;

/// <summary>
/// One time limit shared by the runner processes of a test run (the method batches of one app): each process gets what the
/// ones before it left, so a long list of methods cannot multiply the limit.
/// </summary>
internal sealed class TimeBudget(TimeSpan limit, TimeProvider time)
{
    private readonly long start = time.GetTimestamp();

    /// <summary>The time left, never below zero; <see cref="Timeout.InfiniteTimeSpan"/> when there is no limit.</summary>
    public TimeSpan Left => limit == Timeout.InfiniteTimeSpan ? limit : TimeSpan.FromTicks(Math.Max(0, (limit - time.GetElapsedTime(start)).Ticks));

    /// <summary>Whether the limit is used up: the next process would have no time at all.</summary>
    public bool IsSpent => Left == TimeSpan.Zero;
}
