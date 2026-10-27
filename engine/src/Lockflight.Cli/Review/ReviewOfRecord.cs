using System.Globalization;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Review;

/// <summary>
/// Whether a run counts as the review of record: only with the manifest's (or the default) model and budget, against a base whose
/// commit is the merge base of HEAD and the target branch (so a recent commit on the branch cannot narrow the diff, and an outdated
/// base cannot widen it). Any other run is recorded too, with the reasons it is not of record, so a re-roll with a
/// cheaper model or an old base is visible rather than quietly replacing the review.
/// </summary>
internal static class ReviewOfRecord
{
    private const int ShortCommit = 7;

    public static IReadOnlyList<string> Reasons(ReviewSettings settings, string model, decimal budgetUsd, ReviewBase reviewBase)
    {
        List<string> reasons = [];
        if (!string.Equals(model, settings.Model, StringComparison.Ordinal))
        {
            reasons.Add($"model {model} is not the review model {settings.Model} (review.model)");
        }

        if (budgetUsd != settings.BudgetUsd)
        {
            reasons.Add($"budget ${Usd(budgetUsd)} is not the review budget ${Usd(settings.BudgetUsd)} (review.maxUsd)");
        }

        if (reviewBase.MergeBase is null)
        {
            reasons.Add($"no merge base of HEAD and the target {reviewBase.Target} (review.target)");
        }
        else if (!reviewBase.IsMergeBase)
        {
            reasons.Add($"base {reviewBase.Ref} ({Short(reviewBase.Commit)}) is not the merge base of HEAD and the target {reviewBase.Target} "
                + $"({Short(reviewBase.MergeBase)}): review against the target, after merging or rebasing onto it when it has moved on");
        }

        return reasons;
    }

    public static string Usd(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Short(string? commit) => commit is null ? "unknown" : commit[..Math.Min(ShortCommit, commit.Length)];
}
