using System.Globalization;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle.Fix;

/// <summary>
/// The label of the next red commit and the cap, as the judge counts them (<see cref="RedCommitSelector"/>). The next label is one more
/// than the highest well-formed label on the branch, superseded ones included, so a new red never reuses a label by accident;
/// <c>--redo &lt;n&gt;</c> reuses an existing one on purpose, and the new commit supersedes that cycle's red. The cap counts the
/// cycles (non-superseded labelled commits), not the label numbers: refused when this red would make more than
/// <c>ci.maxRedCommits</c> of the base branch.
/// </summary>
internal static class RedLabels
{
    public static RedLabelChoice Choose(RedCommitSelection reds, int? redo, int maxRedCommits)
    {
        int[] labels = [.. reds.All.Select(red => red.Label).OfType<string>().Select(label => int.Parse(label, NumberStyles.None, CultureInfo.InvariantCulture)).Distinct().Order()];
        int label;
        int cycles;
        if (redo is int again)
        {
            if (!labels.Contains(again))
            {
                string known = labels.Length == 0 ? "the branch has no red commit yet" : $"the labels here are {string.Join(", ", labels)}";
                return RedLabelChoice.Refused(ExitCode.Config, $"--redo {again} names no red commit on this branch ({known}); run without --redo for a new cycle");
            }

            (label, cycles) = (again, reds.Cycles.Count);
        }
        else
        {
            (label, cycles) = (labels.Length == 0 ? 1 : labels[^1] + 1, reds.Cycles.Count + 1);
        }

        return cycles > maxRedCommits
            ? RedLabelChoice.Refused(ExitCode.Fail,
                $"this red would make {cycles} red commits on the branch, over ci.maxRedCommits ({maxRedCommits}) of the base branch; squash earlier reds into one, or split the pull request")
            : RedLabelChoice.Chosen(label);
    }
}
