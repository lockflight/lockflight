using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// The uncommitted changes of the repository a directory lies in — tracked files changed since HEAD (a rename as both paths) and
/// new files git does not ignore — as full paths, with the repository's root reached from the directory (no symbolic link resolved,
/// so the paths compare with the solution's). Null when git cannot say (not a repository, no commit yet).
/// </summary>
internal sealed record WorkingTreeChanges(string RepositoryRoot, IReadOnlyList<string> Files)
{
    public static async Task<WorkingTreeChanges?> ReadAsync(string directory, CancellationToken cancellationToken)
    {
        ProcessResult up = await ProcessRunner.RunAsync("git", ["rev-parse", "--show-cdup"], directory, cancellationToken);
        if (up.ExitCode != 0)
        {
            return null;
        }

        string root = Path.GetFullPath(Path.Combine(directory, up.Output.Trim()));
        IReadOnlyList<string>? changed = await Git.ChangedNamesAsync(root, Git.WorkingTree, Timeout.InfiniteTimeSpan, cancellationToken);
        IReadOnlyList<string>? untracked = await Git.UntrackedFilesAsync(root, cancellationToken);
        if (changed is null || untracked is null)
        {
            return null;
        }

        return new WorkingTreeChanges(root, [.. changed.Concat(untracked)
            .Select(file => Path.GetFullPath(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar))))
            .Distinct(AffectedPaths.Comparer)
            .Order(StringComparer.Ordinal)]);
    }
}
