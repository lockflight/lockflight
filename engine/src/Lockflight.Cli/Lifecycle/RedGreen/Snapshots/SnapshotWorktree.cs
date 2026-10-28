using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Lifecycle.RedGreen.Snapshots;

/// <summary>
/// A temporary, detached git worktree of a red-time snapshot, where an amended test is built and run against the code as it was at
/// red. It lives in the repository's own git directory (<c>.git/lockflight/</c>), not in a shared temporary folder, so a build that
/// looks upward for <c>Directory.Build.props</c>, <c>global.json</c> or <c>nuget.config</c> finds the project's files and never one
/// another user planted. It is checked out without running the repository's hooks (<c>--no-checkout</c>, then <c>reset --hard</c>), and
/// <see cref="RemoveAsync"/> deletes it and git's record of it. The person's working tree, index, branch and stash are never touched.
/// </summary>
internal sealed class SnapshotWorktree
{
    private const string Folder = "lockflight";

    /// <summary>Cleanup runs even after the red was cancelled, within this limit per git call.</summary>
    private static readonly TimeSpan CleanupTimeLimit = TimeSpan.FromSeconds(60);

    private readonly string top;
    private readonly string directory;

    private SnapshotWorktree(string top, string directory, string root)
    {
        this.top = top;
        this.directory = directory;
        Root = root;
    }

    /// <summary>The project root inside the worktree: the worktree itself, or the same subdirectory the project has in its repository.</summary>
    public string Root { get; }

    public static async Task<SnapshotWorktreeOpening> OpenAsync(string root, string commit, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        if (await RedSnapshots.TopLevelAsync(root, timeLimit, cancellationToken) is not string top
            || await SnapshotGit.OutputAsync(["rev-parse", "--git-common-dir"], top, timeLimit, cancellationToken) is not string gitDirectory)
        {
            return SnapshotWorktreeOpening.Failed("the project is not in a git repository any more");
        }

        string shortCommit = RedGreenSession.Short(commit);
        ProcessResult exists = await SnapshotGit.RunAsync(["cat-file", "-e", $"{commit}^{{commit}}"], top, timeLimit, cancellationToken);
        if (exists.ExitCode != 0)
        {
            return SnapshotWorktreeOpening.Failed($"the red-time snapshot {shortCommit} is not in the repository (was its ref under {RedSnapshots.RefPrefix} deleted?)");
        }

        ProcessResult prefix = await SnapshotGit.RunAsync(["rev-parse", "--show-prefix"], root, timeLimit, cancellationToken);
        string directory = Path.Combine(Path.GetFullPath(Path.Combine(top, gitDirectory)), Folder, $"red-{Guid.CreateVersion7()}");
        string worktreeRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(directory, prefix.Output.Trim())));
        SnapshotWorktree worktree = new(top, directory, worktreeRoot);
        try
        {
            ProcessResult added = await SnapshotGit.RunAsync(["worktree", "add", "--detach", "--no-checkout", "--quiet", directory, commit], top, timeLimit, cancellationToken);
            ProcessResult checkedOut = added.ExitCode == 0
                ? await SnapshotGit.RunAsync(["reset", "--hard", "--quiet"], directory, timeLimit, cancellationToken)
                : added;
            if (prefix.ExitCode != 0 || checkedOut.ExitCode != 0)
            {
                await worktree.RemoveAsync();
                return SnapshotWorktreeOpening.Failed($"git could not check out the red-time snapshot {shortCommit}: {checkedOut.Combined.Trim()}");
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            await worktree.RemoveAsync();
            throw;
        }

        return SnapshotWorktreeOpening.Opened(worktree);
    }

    /// <summary>
    /// Removes the worktree; null when it is gone, else what is left behind. It takes no cancellation token on purpose: cleanup must
    /// run after a cancelled red too, so each git call is bounded by <see cref="CleanupTimeLimit"/> instead.
    /// </summary>
    public async Task<string?> RemoveAsync()
    {
        await SnapshotGit.RunAsync(["worktree", "remove", "--force", directory], top, CleanupTimeLimit, CancellationToken.None);
        if (Directory.Exists(directory))
        {
            try
            {
                foreach (FileInfo file in new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    file.Attributes = FileAttributes.Normal;
                }

                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Reported below; a build server can hold a file a moment longer (Windows).
            }
        }

        await SnapshotGit.RunAsync(["worktree", "prune"], top, CleanupTimeLimit, CancellationToken.None);
        try
        {
            if (Path.GetDirectoryName(directory) is string parent && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
            }
        }
        catch (IOException)
        {
            // Another re-check is using the folder; it removes it when it is done.
        }

        return Directory.Exists(directory) ? $"the temporary worktree {directory} could not be removed; delete it and run 'git worktree prune'" : null;
    }
}
