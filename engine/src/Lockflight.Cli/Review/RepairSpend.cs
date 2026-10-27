namespace Lockflight.Cli.Review;

/// <summary>How many reproduction repair calls a run made and what they cost (an estimate at API prices).</summary>
internal sealed record RepairSpend(int Calls, decimal CostUsd)
{
    public static RepairSpend None { get; } = new(0, 0m);
}
