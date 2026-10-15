namespace Lockflight.Cli.Processes;

/// <summary>
/// The <see cref="Git"/> queries over every working tree of a project (<see cref="WorkspaceRepos"/>), with paths relative to the
/// project root: a child repository's paths carry its unit path. Null when git fails in any of them — never "no changes".
/// </summary>
internal static class WorkspaceGit
{
    /// <summary>Files a range changes in every working tree; the same range (a ref name) is asked of each.</summary>
    public static async Task<IReadOnlyList<string>?> ChangedNamesAsync(string projectRoot, string range, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        List<string> names = [];
        IReadOnlyList<RepoRoot> repos = WorkspaceRepos.Of(projectRoot);
        foreach (RepoRoot repo in repos)
        {
            if (await Git.ChangedNamesAsync(repo.Directory, range, timeLimit, cancellationToken) is not IReadOnlyList<string> changed)
            {
                return null;
            }

            names.AddRange(Prefixed(repos, repo, changed));
        }

        return names;
    }

    /// <summary>Untracked, not-ignored files of every working tree, optionally limited to <paramref name="pathspecs"/>.</summary>
    public static async Task<IReadOnlyList<string>?> UntrackedFilesAsync(
        string projectRoot, IReadOnlyList<string> pathspecs, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        List<string> files = [];
        IReadOnlyList<RepoRoot> repos = WorkspaceRepos.Of(projectRoot);
        foreach (RepoRoot repo in repos)
        {
            if (await Git.UntrackedFilesAsync(repo.Directory, pathspecs, timeLimit, cancellationToken) is not IReadOnlyList<string> untracked)
            {
                return null;
            }

            files.AddRange(Prefixed(repos, repo, untracked));
        }

        return files;
    }

    /// <summary>The zero-context diffs of a range in every working tree, one after another, with project-relative file names.</summary>
    public static async Task<string?> DiffHunksAsync(
        string projectRoot, string range, IReadOnlyList<string> pathspecs, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        List<string> diffs = [];
        foreach (RepoRoot repo in WorkspaceRepos.Of(projectRoot))
        {
            if (await Git.DiffHunksAsync(repo.Directory, range, pathspecs, repo.Prefix, timeLimit, cancellationToken) is not string diff)
            {
                return null;
            }

            diffs.Add(diff);
        }

        return string.Join('\n', diffs.Where(diff => diff.Length > 0));
    }

    /// <summary>A working tree's paths under the project root; the root's own entries for child repositories are dropped.</summary>
    private static IEnumerable<string> Prefixed(IReadOnlyList<RepoRoot> repos, RepoRoot repo, IReadOnlyList<string> paths) => repo.Prefix.Length == 0
        ? paths.Where(path => !WorkspaceRepos.InChildRepository(repos, path))
        : paths.Select(path => repo.Prefix + path);
}
