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
                // An index newer than the files it lists would make git trust a stale stat of an edit made in the same tick.
                File.SetLastWriteTimeUtc(index, File.GetLastWriteTimeUtc(realIndex));
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

    /// <summary>
    /// Why the tree would not show a change to an input: one of <paramref name="inputs"/> is git-ignored (a <c>local.props</c> a
    /// <c>Directory.Build.props</c> imports when it exists), or an entry is marked assume-unchanged or skip-worktree (a sparse
    /// checkout), which <c>git add</c> does not read again. Null when neither; a message too when git cannot say.
    /// </summary>
    public static async Task<string?> UnseenInputAsync(string repositoryRoot, IReadOnlyList<string> inputs, Func<string, string> describe, CancellationToken cancellationToken)
    {
        string list = Path.Combine(Path.GetTempPath(), $"lockflight-affected-inputs-{Guid.CreateVersion7()}");
        try
        {
            await File.WriteAllTextAsync(list, string.Concat(inputs.Select(input => Path.GetRelativePath(repositoryRoot, input).Replace('\\', '/') + "\0")), cancellationToken);
            ProcessResult ignored = await ProcessRunner.RunAsync("git", ["check-ignore", "-z", "--stdin"], repositoryRoot, GitTimeLimit, list, cancellationToken);
            if (ignored.ExitCode == 0 && ignored.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is string first)
            {
                return $"{describe(Path.GetFullPath(first, repositoryRoot))} is git-ignored, so its changes are not seen";
            }

            if (ignored.ExitCode > 1)
            {
                return "git could not say which inputs it ignores";
            }
        }
        finally
        {
            File.Delete(list);
        }

        ProcessResult flags = await SnapshotGit.RunAsync(["ls-files", "-v", "-z"], repositoryRoot, GitTimeLimit, cancellationToken);
        if (flags.ExitCode != 0)
        {
            return "git could not list the index";
        }

        return flags.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(entry => entry.Length > 2 && (char.IsLower(entry[0]) || entry[0] == 'S')) is string hidden
            ? $"{hidden[2..]} is marked assume-unchanged or skip-worktree, so git does not read its changes"
            : null;
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
