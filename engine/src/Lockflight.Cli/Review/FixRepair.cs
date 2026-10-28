namespace Lockflight.Cli.Review;

/// <summary>The answer to one fix repair request: whether the reviewer was asked (not when the budget is spent), and the repaired fix when it gave one.</summary>
internal sealed record FixRepair(bool Asked, string? Fix)
{
    public static FixRepair NotAsked { get; } = new(false, null);
}
