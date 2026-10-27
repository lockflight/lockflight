namespace Lockflight.Cli.Tests;

/// <summary>A clock that moves only when a test moves it.</summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>Elapsed time (<see cref="TimeProvider.GetElapsedTime(long)"/>) follows the manual clock too.</summary>
    public override long GetTimestamp() => Now.UtcTicks;

    public void Advance(TimeSpan by) => Now += by;
}
