namespace Lockflight.Cli.Review;

/// <summary>One stored review run as the history lists it; <see cref="NotOfRecord"/> holds why it is not the review of record.</summary>
internal sealed record ReviewRunSummary(
    string Id, DateTimeOffset At, string Model, string BaseRef, string? MergeBase, IReadOnlyList<string> NotOfRecord, decimal EstimatedCostUsd)
{
    public bool OfRecord => NotOfRecord.Count == 0;
}
