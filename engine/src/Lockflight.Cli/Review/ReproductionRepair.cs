namespace Lockflight.Cli.Review;

/// <summary>The answer to one repair request: whether the reviewer was asked (not when the budget is spent), and the repaired reproduction when it gave a usable one.</summary>
internal sealed record ReproductionRepair(bool Asked, string? Reproduction)
{
    public static ReproductionRepair NotAsked { get; } = new(false, null);
}
