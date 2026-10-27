namespace Lockflight.Cli.Review;

/// <summary>The tokens one <c>claude -p</c> call used, summed over the models it ran (<c>modelUsage</c>, else <c>usage</c>).</summary>
internal sealed record ReviewUsage(long InputTokens, long OutputTokens, long CacheReadInputTokens, long CacheCreationInputTokens)
{
    public static ReviewUsage Empty { get; } = new(0, 0, 0, 0);
}
