using System.Globalization;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Review;

/// <summary>
/// Whether a run counts as the review of record: only when the reviewer was shown the whole diff of a clean tree (everything it
/// reads committed), with the manifest's (or the default) model and budget, against a base whose
/// commit is the merge base of HEAD and the target branch (so a recent commit on the branch cannot narrow the diff, and an outdated
/// base cannot widen it). Any other run is recorded too, with the reasons it is not of record, so a re-roll with a
/// cheaper model or an old base is visible rather than quietly replacing the review.
/// </summary>
internal static class ReviewOfRecord
{
    private const int ShortCommit = 7;
    private const int MaxNamedFiles = 3;

    public static IReadOnlyList<string> Reasons(ReviewSettings settings, string model, decimal budgetUsd, ReviewBase reviewBase, ReviewDiff diff, ReviewedTree tree)
    {
        List<string> reasons = [];
        if (!diff.IsComplete)
        {
            reasons.Add(Incomplete(diff));
        }

        if (!tree.IsClean)
        {
            reasons.Add(NotCommitted(tree));
        }

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

    /// <summary>Why a run whose reviewer was not shown the whole diff is not of record, and what to do instead.</summary>
    public static string Incomplete(ReviewDiff diff)
    {
        string named = string.Join(", ", diff.NotShown.Take(MaxNamedFiles));
        string more = diff.NotShown.Count > MaxNamedFiles ? $" and {diff.NotShown.Count - MaxNamedFiles} more" : string.Empty;
        return $"the diff ({diff.Length} characters) is larger than the {diff.Limit} the reviewer is shown, so the reviewer did not see the whole change "
            + $"({diff.NotShown.Count} of {diff.Files} changed files not shown in full: {named}{more}): split the change into smaller pull requests "
            + "and review each; a review against a later commit of this branch (--base <commit>) checks only the commits after it and is not of record";
    }

    /// <summary>Why a run over changes no commit holds is not of record, and how to make the tree clean.</summary>
    public static string NotCommitted(ReviewedTree tree)
    {
        if (!tree.Checked)
        {
            return "git could not list the uncommitted changes, so it is unknown whether the review is of a commit: run 'git status' to see why";
        }

        List<string> parts = [];
        if (tree.Uncommitted.Count > 0)
        {
            parts.Add($"{tree.Uncommitted.Count} uncommitted: {Named(tree.Uncommitted)}");
        }

        if (tree.Untracked.Count > 0)
        {
            parts.Add($"{tree.Untracked.Count} untracked, which the reviewer was not shown: {Named(tree.Untracked)}");
        }

        return $"the working tree has changes that are not committed in the paths the review reads ({string.Join("; ", parts)}), so the run "
            + "does not review a commit: commit them, or stash them ('git stash -u' also stashes untracked files), and review again "
            + "(ignored files, .lockflight/ and .claude/worktrees/ do not count)";
    }

    public static string Usd(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Short(string? commit) => commit is null ? "unknown" : commit[..Math.Min(ShortCommit, commit.Length)];

    private static string Named(IReadOnlyList<string> paths)
    {
        string named = string.Join(", ", paths.Take(MaxNamedFiles));
        return paths.Count > MaxNamedFiles ? $"{named} and {paths.Count - MaxNamedFiles} more" : named;
    }
}
