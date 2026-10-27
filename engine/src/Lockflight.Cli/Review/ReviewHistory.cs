namespace Lockflight.Cli.Review;

/// <summary>A feature's review runs, oldest first, and where its tracked findings stand after them and after every dismissal.</summary>
internal sealed record ReviewHistory(
    IReadOnlyList<ReviewRunSummary> Runs, IReadOnlyList<TrackedFinding> Open, IReadOnlyList<TrackedFinding> Fixed, IReadOnlyList<FindingDismissal> Dismissed)
{
    public static ReviewHistory Empty { get; } = new([], [], [], []);

    public bool IsOpen(string id) => Open.Any(tracked => tracked.Finding.Id == id);

    public bool IsDismissed(string id) => Dismissed.Any(dismissal => dismissal.FindingId == id);
}
