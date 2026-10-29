using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Lifecycle.RedGreen.Snapshots;

/// <summary>
/// Commits the tree an accepted red judged, with a <c>Lockflight-Red: &lt;cycle&gt;</c> trailer, so CI can re-run the red at that
/// commit. The commit is made through a private index read from the red-time snapshot, with the person's own git configuration
/// (signing) and hooks; the person's index keeps everything they had staged, and only the paths the red commit holds are reset to
/// it. The engine never bypasses a hook: when one refuses, the red stays accepted and the person gets the line to commit it.
/// </summary>
internal static class RedCommitter
{
    public const string Trailer = "Lockflight-Red";

    private const int PathsPerCall = 200;

    /// <summary>
    /// Why a red with red commits on cannot be committed, said before its build: a detached HEAD, a merge, rebase or cherry-pick in
    /// progress, or untracked files outside the project directories (which the snapshot, and so the commit, would hold). Null when
    /// it can, or outside a git repository (no snapshot, nothing to commit).
    /// </summary>
    public static async Task<string?> RefusalAsync(string root, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        // Outside a repository there is no snapshot and nothing to commit; the red goes on as before.
        if ((await SnapshotGit.RunAsync(["rev-parse", "--git-dir"], root, timeLimit, cancellationToken)).ExitCode != 0)
        {
            return null;
        }

        if ((await SnapshotGit.RunAsync(["symbolic-ref", "-q", "HEAD"], root, timeLimit, cancellationToken)).ExitCode != 0)
        {
            return "HEAD is detached; check out the feature branch, then run the red again (the accepted red is committed on it)";
        }

        foreach (string state in new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply" })
        {
            ProcessResult path = await SnapshotGit.RunAsync(["rev-parse", "--git-path", state], root, timeLimit, cancellationToken);
            if (path.ExitCode == 0 && Path.Exists(Path.GetFullPath(path.Output.Trim(), root)))
            {
                return $"a {(state.StartsWith("rebase", StringComparison.Ordinal) ? "rebase" : state[..state.IndexOf('_', StringComparison.Ordinal)].ToLowerInvariant())} is in progress; finish or abort it, then run the red again";
            }
        }

        ProcessResult listing = await SnapshotGit.RunAsync(["ls-files", "--others", "--exclude-standard", "-z", "--", ":(exclude,glob)**/bin/**", ":(exclude,glob)**/obj/**"], root, timeLimit, cancellationToken);
        string[] projects = [.. Directory.EnumerateFiles(root, "*.*proj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, Path.GetDirectoryName(path)!).Replace('\\', '/'))
            .Where(directory => !directory.Split('/').Any(segment => segment is "bin" or "obj"))];
        string[] stray = [.. listing.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(path => !Allowed(path, projects))];
        return stray.Length == 0
            ? null
            : $"untracked files outside the project directories would go into the red commit: {string.Join(", ", stray.Take(RedGreenSession.MaxTestsListed))}"
              + $"{(stray.Length > RedGreenSession.MaxTestsListed ? $" and {stray.Length - RedGreenSession.MaxTestsListed} more" : string.Empty)}; add them to .gitignore, or commit them yourself, then run the red again";
    }

    /// <summary>Commits the snapshot's tree on HEAD with the red's message and trailer.</summary>
    public static async Task<RedCommitOutcome> CommitAsync(string root, string snapshot, string featureId, int cycle, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        string subject = $"test({featureId}): red for cycle {cycle}";
        string trailer = $"{Trailer}: {cycle}";
        string index = Path.Combine(Path.GetTempPath(), $"lockflight-red-index-{Guid.CreateVersion7()}");
        try
        {
            Dictionary<string, string> privateIndex = new(StringComparer.Ordinal) { [SnapshotGit.IndexVariable] = index };
            if ((await SnapshotGit.RunAsync(["read-tree", $"{snapshot}^{{tree}}"], root, timeLimit, privateIndex, cancellationToken)).ExitCode != 0)
            {
                return new RedCommitOutcome(null, "git could not read the red-time tree into an index; commit the red yourself with the trailer " + trailer);
            }

            ProcessResult commit = await SnapshotGit.RunAsync(["commit", "-q", "-m", subject, "-m", trailer], root, timeLimit, privateIndex, cancellationToken);
            if (commit.ExitCode != 0)
            {
                return new RedCommitOutcome(null,
                    $"git commit refused the red commit: {commit.Combined.Trim()}. A pre-commit hook that runs the tests refuses a red by nature, and a red commit "
                    + $"fails its tests on purpose, so you may commit it without hooks: git add -A && git commit --no-verify -m \"{subject}\" -m \"{trailer}\"");
            }

            string head = (await SnapshotGit.RunAsync(["rev-parse", "HEAD"], root, timeLimit, cancellationToken)).Output.Trim();
            await KeepIndexAsync(root, timeLimit, cancellationToken);
            ProcessResult committedTree = await SnapshotGit.RunAsync(["rev-parse", "HEAD^{tree}"], root, timeLimit, cancellationToken);
            ProcessResult judgedTree = await SnapshotGit.RunAsync(["rev-parse", $"{snapshot}^{{tree}}"], root, timeLimit, cancellationToken);
            string note = committedTree.Output.Trim() == judgedTree.Output.Trim()
                ? $"Red commit {RedGreenSession.Short(head)} ({subject}, {trailer}): the tree this red judged; CI re-runs the red there."
                : $"Red commit {RedGreenSession.Short(head)} differs from the tree this red judged (a hook changed it); CI judges the commit as it is.";
            return new RedCommitOutcome(head, note);
        }
        finally
        {
            File.Delete(index);
        }
    }

    /// <summary>Resets the person's index to the new commit for the paths the red commit holds, keeping everything else staged.</summary>
    private static async Task KeepIndexAsync(string root, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        ProcessResult changed = await SnapshotGit.RunAsync(["diff", "--name-only", "-z", "--no-renames", "HEAD^", "HEAD"], root, timeLimit, cancellationToken);
        foreach (string[] chunk in changed.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Chunk(PathsPerCall))
        {
            await SnapshotGit.RunAsync(["reset", "-q", "HEAD", "--", .. chunk.Select(path => $":(literal){path}")], root, timeLimit, cancellationToken);
        }
    }

    /// <summary>A new file inside a project directory, or one of the feature's specs or records, belongs in the red commit.</summary>
    private static bool Allowed(string path, string[] projects) =>
        path.StartsWith("specs/", StringComparison.Ordinal)
        || path.StartsWith(".lockflight/features/", StringComparison.Ordinal)
        || projects.Any(directory => directory == "." || path.StartsWith(directory + "/", StringComparison.Ordinal));
}
