using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Review;

/// <summary>
/// Finds what a review would read that is not committed: the paths of its diff (everything but <c>.lockflight/</c> and
/// <c>specs/</c> in the project's repository, everything in a unit's own repository) and the feature's spec. Files git ignores do
/// not count, nor do the engine's and Claude Code's own paths (<c>.lockflight/</c>, <c>.claude/worktrees/</c>) or untracked build
/// output (<c>bin/</c>, <c>obj/</c>), which no diff shows.
/// </summary>
internal static class ReviewedTrees
{
    private static readonly string[] OwnPaths = [".lockflight/", ".claude/worktrees/"];

    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(60);

    public static async Task<ReviewedTree> FindAsync(string root, string specPath, CancellationToken cancellationToken)
    {
        string spec = Path.GetRelativePath(root, specPath).Replace('\\', '/');
        IReadOnlyList<RepoRoot> repos = WorkspaceRepos.Of(root);
        List<string> uncommitted = [];
        List<string> untracked = [];
        foreach (RepoRoot repo in repos)
        {
            if (await Git.UncommittedBaseAsync(repo.Directory, cancellationToken) is not string baseTree
                || await Git.ChangedNamesAsync(repo.Directory, baseTree, TimeLimit, cancellationToken) is not IReadOnlyList<string> changed
                || await Git.UntrackedFilesAsync(repo.Directory, [], TimeLimit, cancellationToken) is not IReadOnlyList<string> others)
            {
                return ReviewedTree.Unchecked;
            }

            bool isRoot = repo.Prefix.Length == 0;
            uncommitted.AddRange(changed.Where(path => !isRoot || IsReviewed(repos, path, spec)).Select(path => repo.Prefix + path));
            untracked.AddRange(others.Where(path => !IsBuildOutput(path) && (!isRoot || IsReviewed(repos, path, spec))).Select(path => repo.Prefix + path));
        }

        return new ReviewedTree(true, [.. uncommitted.Order(StringComparer.Ordinal)], [.. untracked.Order(StringComparer.Ordinal)]);
    }

    /// <summary>Whether a path of the project's own repository is one the review reads.</summary>
    private static bool IsReviewed(IReadOnlyList<RepoRoot> repos, string path, string spec) =>
        !OwnPaths.Any(own => path.StartsWith(own, StringComparison.Ordinal))
        && !WorkspaceRepos.InChildRepository(repos, path)
        && (!path.StartsWith(FeatureLocator.SpecsDirectory + "/", StringComparison.Ordinal) || path == spec);

    private static bool IsBuildOutput(string path) => path.Split('/').Any(segment => segment is "bin" or "obj");
}
