using System.Globalization;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Picks the red commits from a pull request's history (oldest first, topological order): one well-formed <c>Lockflight-Red:
/// &lt;cycle&gt;</c> trailer marks a red; the last commit with a label is that cycle's red and earlier ones are superseded; the
/// cycle order is the commits' order, not the label numbers.
/// </summary>
internal static partial class RedCommitSelector
{

    public static RedCommitSelection Select(IReadOnlyList<CommitTrailers> history, int maxRedCommits)
    {
        List<RedCommit> marked = [.. history.Where(commit => commit.Red.Count > 0).Select(Read)];
        Dictionary<string, int> lastIndexOfLabel = new(StringComparer.Ordinal);
        for (int index = 0; index < marked.Count; index++)
        {
            if (marked[index].Label is string label)
            {
                lastIndexOfLabel[label] = index;
            }
        }

        RedCommit[] all = [.. marked.Select((red, index) => red.Label is string label && lastIndexOfLabel[label] != index ? red with { Superseded = true } : red)];
        RedCommit[] cycles = [.. all.Where(red => red.IsCycle)];
        int[] numbers = [.. cycles.Select(red => int.Parse(red.Label!, NumberStyles.None, CultureInfo.InvariantCulture))];
        bool outOfOrder = numbers.Zip(numbers.Skip(1)).Any(pair => pair.Second < pair.First);
        string[] amends = [.. history.Where(commit => commit.Amend.Count > 0).Select(commit => commit.Commit)];
        return new RedCommitSelection(all, cycles, amends, cycles.Length > maxRedCommits, outOfOrder);
    }

    private static RedCommit Read(CommitTrailers commit)
    {
        if (commit.Red.Count > 1)
        {
            return new RedCommit(commit.Commit, commit.Subject, commit.CommittedAt, Label: null, $"{commit.Red.Count} Lockflight-Red trailers; a red commit has exactly one", Superseded: false);
        }

        string value = commit.Red[0];
        string? malformed = CycleLabel().IsMatch(value) ? null
            : FeatureLabel().IsMatch(value) ? $"'{value}': the feature/cycle form is reserved for a pull request with more than one feature; use the cycle number alone"
            : $"'{value}' is not a cycle number (a positive whole number such as 2)";
        return new RedCommit(commit.Commit, commit.Subject, commit.CommittedAt, malformed is null ? value : null, malformed, Superseded: false);
    }

    [GeneratedRegex(@"^[1-9][0-9]{0,5}$")]
    private static partial Regex CycleLabel();

    [GeneratedRegex(@"^[0-9]{3}-[A-Za-z0-9._-]+/[1-9][0-9]*$")]
    private static partial Regex FeatureLabel();
}
