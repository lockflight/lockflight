namespace Lockflight.Cli.Canaries;

/// <summary>A clock a fixed span ahead of another: how a canary lets a one-time code expire without waiting for it.</summary>
internal sealed class ShiftedTime(TimeProvider clock, TimeSpan offset) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => clock.GetUtcNow() + offset;
}
