using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Reads the spec diff of a pull request (merge base to head) for the paths the target branch's <c>approvals.specPaths</c> names:
/// the spec is what a person approves, so the comment shows its change before anything else. A unit repository of a workspace shell
/// holds no spec (the shell does), and a push or a pull request that changes no spec has no spec diff.
/// </summary>
internal static class SpecDiffReader
{
    /// <summary>The longest spec diff kept in the evidence; the job summary shows it whole, the comment collapses a long one.</summary>
    public const int MaxLength = 60_000;

    /// <summary>A path from the pull request is a path to git, never a pattern.</summary>
    private const string LiteralPathspec = ":(literal)";

    public static async Task<SpecDiff?> ReadAsync(JudgePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Inputs.Event != JudgeEvent.PullRequest || plan.Shell is not null || plan.MergeBase is not string mergeBase)
        {
            return null;
        }

        IReadOnlyList<string> globs = (plan.Policy.Manifest?.Approvals ?? ApprovalSettings.Default).SpecPaths;
        string[] paths = [.. plan.ChangedFiles.Where(path => globs.Any(glob => PathRules.Matches(glob, path))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        if (paths.Length == 0
            || await JudgeGit.DiffAsync(plan.Inputs.Root, mergeBase, plan.Inputs.Head, [.. paths.Select(path => LiteralPathspec + path)], cancellationToken) is not { Length: > 0 } text)
        {
            return null;
        }

        int lines = text.Count(character => character == '\n');
        if (text.Length <= MaxLength)
        {
            return new SpecDiff(paths, text, lines, Truncated: false);
        }

        int lastLine = text.LastIndexOf('\n', MaxLength - 1);
        return new SpecDiff(paths, lastLine > 0 ? text[..(lastLine + 1)] : text[..MaxLength] + "\n", lines, Truncated: true);
    }
}
