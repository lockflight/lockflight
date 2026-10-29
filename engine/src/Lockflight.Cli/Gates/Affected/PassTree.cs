using Lockflight.Cli.Lifecycle.RedGreen.Snapshots;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// The working tree of a repository as a git tree object — tracked files as they are now and new files git does not ignore,
/// written through a private copy of the index, so the person's index is untouched — and the files that differ between two such
/// trees. The tree of a target's last stop PASS (<see cref="StopPassRecord"/>) against the tree now names everything changed
/// since the gate was last green: edits, commits, reverts and deletions alike.
/// </summary>
internal static class PassTree
{
    private static readonly TimeSpan GitTimeLimit = TimeSpan.FromSeconds(120);

    /// <summary>The root of the repository the directory lies in, reached from it (no symbolic link resolved); null outside git.</summary>
    public static async Task<string?> RepositoryRootAsync(string directory, CancellationToken cancellationToken)
    {
        ProcessResult up = await SnapshotGit.RunAsync(["rev-parse", "--show-cdup"], directory, GitTimeLimit, cancellationToken);
        return up.ExitCode == 0 ? Path.GetFullPath(Path.Combine(directory, up.Output.Trim())) : null;
    }

    /// <summary>The working tree's tree id; null when git cannot write it (no repository, an unreadable file).</summary>
    public static async Task<string?> CurrentAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        string index = Path.Combine(Path.GetTempPath(), $"lockflight-affected-index-{Guid.CreateVersion7()}");
        try
        {
            // Starting from the real index keeps its stat cache: only files that changed are hashed again.
            if (await SnapshotGit.OutputAsync(["rev-parse", "--git-path", "index"], repositoryRoot, GitTimeLimit, cancellationToken) is string real
                && Path.GetFullPath(real, repositoryRoot) is string realIndex && File.Exists(realIndex))
            {
                File.Copy(realIndex, index);
            }

            Dictionary<string, string> privateIndex = new(StringComparer.Ordinal) { [SnapshotGit.IndexVariable] = index };
            ProcessResult added = await SnapshotGit.RunAsync(["add", "-A", "--", "."], repositoryRoot, GitTimeLimit, privateIndex, cancellationToken);
            return added.ExitCode == 0 ? await SnapshotGit.OutputAsync(["write-tree"], repositoryRoot, GitTimeLimit, privateIndex, cancellationToken) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            File.Delete(index);
        }
    }

    /// <summary>The full paths of the files that differ between two trees (a rename as both paths); null when git cannot compare them.</summary>
    public static async Task<IReadOnlyList<string>?> ChangedAsync(string repositoryRoot, string before, string after, CancellationToken cancellationToken)
    {
        ProcessResult diff = await SnapshotGit.RunAsync(
            ["-c", "core.quotePath=false", "diff", "--name-only", "-z", "--no-renames", "--no-ext-diff", before, after, "--"], repositoryRoot, GitTimeLimit, cancellationToken);
        return diff.ExitCode != 0 ? null
            : [.. diff.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(file => Path.GetFullPath(Path.Combine(repositoryRoot, file.Replace('/', Path.DirectorySeparatorChar))))
                .Order(StringComparer.Ordinal)];
    }
}
