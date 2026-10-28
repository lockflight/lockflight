namespace Lockflight.Cli.Review;

/// <summary>How many fix checks a run made and the wall time they took, builds and test runs included (milliseconds).</summary>
internal sealed record FixCheckSpend(int Count, long WallTimeMs)
{
    public static FixCheckSpend None { get; } = new(0, 0);
}
