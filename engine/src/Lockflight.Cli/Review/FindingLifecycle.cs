namespace Lockflight.Cli.Review;

/// <summary>
/// How tracked findings move between runs. A blocker or major finding that is confirmed or unverified opens, and stays open in every
/// later run until its reproduction, re-run by a later review, passes (fixed) or a person dismisses it. A run that does not raise an
/// open finding again carries it, so re-running the review never makes one disappear. Only that deterministic re-run closes a finding
/// by itself, and a confirmed finding whose reproduction cannot run this time stays confirmed.
/// </summary>
internal static class FindingLifecycle
{
    /// <summary>
    /// The run's own findings (one per id, the most severe first) plus every open finding it did not raise again. An open finding
    /// raised again keeps the higher of its two severities, and its reproduction when the new one has none, so a reviewer that
    /// re-raises a confirmed major as minor cannot close it.
    /// </summary>
    public static IReadOnlyList<ReviewFinding> Carry(IReadOnlyList<ReviewFinding> fresh, ReviewHistory history)
    {
        ReviewFinding[] own = [.. fresh.OrderBy(finding => finding.Severity).DistinctBy(finding => finding.Id)
            .Select(finding => history.Open.FirstOrDefault(tracked => tracked.Finding.Id == finding.Id) is TrackedFinding open ? RaisedAgain(finding, open.Finding) : finding)];
        HashSet<string> raised = [.. own.Select(finding => finding.Id)];
        IEnumerable<ReviewFinding> carried = history.Open
            .Where(tracked => !raised.Contains(tracked.Finding.Id) && !history.IsDismissed(tracked.Finding.Id))
            .Select(tracked => tracked.Finding with { Carried = true, Outcome = FindingOutcome.Reported, Reproduced = ReproductionResult.None, State = null });
        return [.. own, .. carried];
    }

    /// <summary>Sets each judged finding's state after this run.</summary>
    public static IReadOnlyList<ReviewFinding> Resolve(IReadOnlyList<ReviewFinding> judged, ReviewHistory history) =>
        [.. judged.Select(finding => Resolve(finding, history))];

    private static ReviewFinding RaisedAgain(ReviewFinding finding, ReviewFinding open) => finding with
    {
        Severity = finding.Severity < open.Severity ? finding.Severity : open.Severity,
        Reproduction = finding.Reproduction ?? open.Reproduction,
    };

    private static ReviewFinding Resolve(ReviewFinding finding, ReviewHistory history)
    {
        if (!finding.NeedsReproduction)
        {
            return finding with { State = null };
        }

        if (history.IsDismissed(finding.Id))
        {
            return finding with { State = FindingState.Dismissed };
        }

        if (history.Open.FirstOrDefault(tracked => tracked.Finding.Id == finding.Id) is TrackedFinding open)
        {
            return finding.Outcome switch
            {
                FindingOutcome.Rejected => finding with { State = FindingState.Fixed },
                FindingOutcome.Unverified when open.Finding.Outcome == FindingOutcome.Confirmed => finding with { Outcome = FindingOutcome.Confirmed, State = FindingState.Open },
                _ => finding with { State = FindingState.Open },
            };
        }

        return finding.Outcome is FindingOutcome.Confirmed or FindingOutcome.Unverified
            ? finding with { State = FindingState.Open }
            : finding with { State = null };
    }
}
