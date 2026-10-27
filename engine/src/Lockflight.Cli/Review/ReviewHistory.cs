namespace Lockflight.Cli.Review;

/// <summary>A feature's review runs, oldest first, and where its tracked findings stand after them and after every dismissal.</summary>
internal sealed record ReviewHistory(
    IReadOnlyList<ReviewRunSummary> Runs, IReadOnlyList<TrackedFinding> Open, IReadOnlyList<TrackedFinding> Fixed, IReadOnlyList<FindingDismissal> Dismissed)
{
    public static ReviewHistory Empty { get; } = new([], [], [], []);

    /// <summary>Run files that could not be read, each with its path under the feature's record directory and why; they count for nothing.</summary>
    public IReadOnlyList<string> Unreadable { get; init; } = [];

    public bool IsOpen(string id) => Open.Any(tracked => tracked.Finding.Id == id);

    public bool IsDismissed(string id) => Dismissed.Any(dismissal => dismissal.FindingId == id);
}
