using Lockflight.Cli.Init;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Lifecycle.RedGreen.Snapshots;

/// <summary>
/// Red-time snapshots. Before a red builds, the working tree (tracked, staged and untracked files that are not ignored, without build
/// output) is written as a commit object through a private copy of the index, so the person's index, branch and stash stay as they
/// are; when the red is accepted, a ref keeps it: <c>refs/lockflight/red/&lt;feature&gt;/&lt;cycle&gt;/&lt;commit&gt;</c> (the commit in the name,
/// so linked worktrees on the same feature do not overwrite each other's). The ref is local: Lockflight never pushes it. A re-check
/// after an amend builds this tree with the amended test files laid over it (<see cref="SnapshotWorktree"/>), so an implementation
/// written since the red cannot make the amended test pass.
/// </summary>
internal static class RedSnapshots
{
    public const string RefPrefix = "refs/lockflight/red/";

    private const string Author = "Lockflight";
    private const string AuthorEmail = "lockflight@localhost";
    private const int RefCommitLength = 12;

    /// <summary>The engine's own records; never named as stray files.</summary>
    private const string EngineDirectory = ".lockflight/";

    /// <summary>
    /// Build output and the local files <c>init</c> keeps out of git (the linked engine, the ledger, agent worktrees) never belong in
    /// the snapshot, even in a repository that does not ignore them.
    /// </summary>
    private static readonly string[] LeftOut =
        [":(exclude,glob)**/bin/**", ":(exclude,glob)**/obj/**", .. Templates.GitIgnoreEntries.Select(entry => $":(exclude,glob)**/{entry.TrimEnd('/')}/**")];

    /// <summary>Untracked files with these extensions are the feature's new code, tests or documents; any other is named.</summary>
    private static readonly HashSet<string> SourceExtensions = new(
        [".cs", ".csproj", ".fs", ".fsproj", ".vb", ".vbproj", ".props", ".targets", ".sln", ".slnx", ".resx", ".razor", ".cshtml", ".md"],
        StringComparer.OrdinalIgnoreCase);

    public static string RefFor(string featureId, int cycle, string commit) =>
        $"{RefPrefix}{featureId}/{cycle}/{commit[..Math.Min(RefCommitLength, commit.Length)]}";

    /// <summary>
    /// Writes the snapshot commit of the repository that holds <paramref name="root"/> (no ref yet: <see cref="KeepAsync"/>); skipped,
    /// with the reason, outside a repository, when a directory the red builds (<paramref name="buildDirectories"/>) belongs to another
    /// repository, as in a workspace of several, or when git or the file system fails.
    /// </summary>
    public static async Task<RedSnapshot> TakeAsync(string root, IReadOnlyList<string> buildDirectories, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        if (await TopLevelAsync(root, timeLimit, cancellationToken) is not string top)
        {
            return RedSnapshot.Skipped("not a git repository");
        }

        foreach (string directory in buildDirectories)
        {
            if (await TopLevelAsync(directory, timeLimit, cancellationToken) != top)
            {
                return RedSnapshot.Skipped(
                    $"{Path.GetRelativePath(root, directory).Replace('\\', '/')} is in another git repository, so there is no single red-time tree");
            }
        }

        ProcessResult listing = await SnapshotGit.RunAsync(["ls-files", "--others", "--exclude-standard", "-z", "--", .. LeftOut], top, timeLimit, cancellationToken);
        string[] untracked = listing.ExitCode == 0 ? listing.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries) : [];
        string index = Path.Combine(Path.GetTempPath(), $"lockflight-index-{Guid.CreateVersion7()}");
        try
        {
            return await WriteAsync(top, index, timeLimit, cancellationToken) is string commit
                ? RedSnapshot.Taken(commit, [.. untracked.Where(IsNotSource)])
                : RedSnapshot.Skipped("git could not write the working tree as a commit");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RedSnapshot.Skipped($"the private index could not be prepared: {exception.Message}");
        }
        finally
        {
            File.Delete(index);
        }
    }

    /// <summary>Points the cycle's ref at the snapshot, so git keeps it; the ref, or null when git refuses.</summary>
    public static async Task<string?> KeepAsync(string root, string featureId, int cycle, string commit, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        string reference = RefFor(featureId, cycle, commit);
        ProcessResult kept = await SnapshotGit.RunAsync(["update-ref", "-m", "lockflight: red accepted", reference, commit], root, timeLimit, cancellationToken);
        return kept.ExitCode == 0 ? reference : null;
    }

    /// <summary>
    /// The unified diff of <paramref name="files"/> (relative to <paramref name="root"/>, '/'-separated) from the snapshot to their
    /// current content, with paths relative to the root; null when git or the file system fails. The current content is staged into a
    /// private index read from the snapshot, so untracked files compare too and the person's index stays as it is.
    /// </summary>
    public static async Task<string?> DiffAsync(string root, string commit, IReadOnlyList<string> files, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        string index = Path.Combine(Path.GetTempPath(), $"lockflight-index-{Guid.CreateVersion7()}");
        try
        {
            Dictionary<string, string> privateIndex = new(StringComparer.Ordinal) { [SnapshotGit.IndexVariable] = index };
            if ((await SnapshotGit.RunAsync(["read-tree", commit], root, timeLimit, privateIndex, cancellationToken)).ExitCode != 0
                || (await SnapshotGit.RunAsync(["add", "-A", "--force", "--", .. files], root, timeLimit, privateIndex, cancellationToken)).ExitCode != 0)
            {
                return null;
            }

            ProcessResult diff = await SnapshotGit.RunAsync(
                ["-c", "core.quotePath=false", "diff", "--cached", "--no-color", "--no-ext-diff", "--no-renames", "--relative", "--src-prefix=a/", "--dst-prefix=b/", commit, "--", .. files],
                root,
                timeLimit,
                privateIndex,
                cancellationToken);
            return diff.ExitCode == 0 ? diff.Output : null;
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

    /// <summary>The repository's top directory as git names it; null outside a repository.</summary>
    public static async Task<string?> TopLevelAsync(string directory, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        Directory.Exists(directory) ? await SnapshotGit.OutputAsync(["rev-parse", "--show-toplevel"], directory, timeLimit, cancellationToken) : null;

    /// <summary>
    /// Stages the working tree into a copy of the index and writes it as a tree and a commit on top of HEAD (none before the first
    /// commit); null when any step fails.
    /// </summary>
    private static async Task<string?> WriteAsync(string top, string index, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        if (await SnapshotGit.OutputAsync(["rev-parse", "--git-path", "index"], top, timeLimit, cancellationToken) is string indexPath
            && Path.Combine(top, indexPath) is string personIndex
            && File.Exists(personIndex))
        {
            // A copy keeps the file stat cache, so staging only reads the files that changed.
            File.Copy(personIndex, index, overwrite: true);
        }

        Dictionary<string, string> privateIndex = new(StringComparer.Ordinal) { [SnapshotGit.IndexVariable] = index };
        if ((await SnapshotGit.RunAsync(["add", "-A", "--", ".", .. LeftOut], top, timeLimit, privateIndex, cancellationToken)).ExitCode != 0
            || await SnapshotGit.OutputAsync(["write-tree"], top, timeLimit, privateIndex, cancellationToken) is not string tree)
        {
            return null;
        }

        string? head = await SnapshotGit.OutputAsync(["rev-parse", "--verify", "--quiet", "HEAD^{commit}"], top, timeLimit, cancellationToken);
        Dictionary<string, string> identity = new(StringComparer.Ordinal)
        {
            ["GIT_AUTHOR_NAME"] = Author,
            ["GIT_AUTHOR_EMAIL"] = AuthorEmail,
            ["GIT_COMMITTER_NAME"] = Author,
            ["GIT_COMMITTER_EMAIL"] = AuthorEmail,
        };
        string[] parent = head is null ? [] : ["-p", head];
        return await SnapshotGit.OutputAsync(["commit-tree", "--no-gpg-sign", tree, .. parent, "-m", "lockflight: red-time snapshot"], top, timeLimit, identity, cancellationToken);
    }

    private static bool IsNotSource(string path) =>
        !path.StartsWith(EngineDirectory, StringComparison.Ordinal) && !SourceExtensions.Contains(Path.GetExtension(path));
}
