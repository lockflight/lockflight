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
    /// progress, or untracked files outside the project directories or that do not look like source (which the snapshot, and so
    /// the pushed commit, would hold). Null when
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

        foreach (string state in new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply", "sequencer" })
        {
            ProcessResult path = await SnapshotGit.RunAsync(["rev-parse", "--git-path", state], root, timeLimit, cancellationToken);
            if (path.ExitCode == 0 && Path.Exists(Path.GetFullPath(path.Output.Trim(), root)))
            {
                string operation = state switch
                {
                    "sequencer" => "cherry-pick or revert",
                    _ when state.StartsWith("rebase", StringComparison.Ordinal) => "rebase",
                    _ => state[..state.IndexOf('_', StringComparison.Ordinal)].ToLowerInvariant(),
                };
                return $"a {operation} is in progress; finish or abort it, then run the red again";
            }
        }

        // The snapshot stages the whole repository from its top level, so the untracked files are listed from there too.
        string prefix = (await SnapshotGit.OutputAsync(["rev-parse", "--show-prefix"], root, timeLimit, cancellationToken)) ?? string.Empty;
        string? top = await RedSnapshots.TopLevelAsync(root, timeLimit, cancellationToken);
        if (top is null)
        {
            return "git could not name the repository's top directory; the red cannot be committed";
        }

        ProcessResult listing = await SnapshotGit.RunAsync(["ls-files", "--others", "--exclude-standard", "-z", "--", .. RedSnapshots.LeftOut], top, timeLimit, cancellationToken);
        if (listing.ExitCode != 0)
        {
            return $"git could not list the untracked files: {listing.Combined.Trim()}";
        }

        string[] allowed = [
            prefix + "specs/",
            prefix + ".lockflight/features/",
            .. Directory.EnumerateFiles(root, "*.*proj", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Select(path => Path.GetRelativePath(root, Path.GetDirectoryName(path)!).Replace('\\', '/'))
                .Where(directory => !directory.Split('/').Any(segment => segment is "bin" or "obj"))
                .Select(directory => directory == "." ? prefix : $"{prefix}{directory}/"),
        ];
        // A file in a project directory goes in too, and the red commit is pushed with the branch: one that does not look like
        // source (a local settings file, a certificate, a dump) is refused as well.
        string[] stray = [.. listing.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !allowed.Any(start => path.StartsWith(start, StringComparison.Ordinal)) || RedSnapshots.IsNotSource(path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path))];
        return stray.Length == 0
            ? null
            : $"untracked files outside the project directories, or that do not look like source, would go into the red commit: {string.Join(", ", stray.Take(RedGreenSession.MaxTestsListed))}"
              + $"{(stray.Length > RedGreenSession.MaxTestsListed ? $" and {stray.Length - RedGreenSession.MaxTestsListed} more" : string.Empty)}; add them to .gitignore, or commit them yourself, then run the red again";
    }

    /// <summary>
    /// Commits the snapshot's tree with the red's message and trailer onto the commit the red started from (the snapshot's parent),
    /// on the branch HEAD named then. When HEAD has moved since (another commit, another branch, a detached HEAD), nothing is
    /// committed: the red stays accepted and the person gets the line that commits the judged tree once they are back on it.
    /// </summary>
    public static async Task<RedCommitOutcome> CommitAsync(string root, string snapshot, string featureId, int cycle, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        string subject = $"test({featureId}): red for cycle {cycle}";
        string trailer = $"{Trailer}: {cycle}";
        string? started = await SnapshotGit.OutputAsync(["rev-parse", "--verify", "--quiet", $"{snapshot}^1"], root, timeLimit, cancellationToken);
        string? branch = await SnapshotGit.OutputAsync(["symbolic-ref", "-q", "HEAD"], root, timeLimit, cancellationToken);
        string? head = await SnapshotGit.OutputAsync(["rev-parse", "--verify", "--quiet", "HEAD^{commit}"], root, timeLimit, cancellationToken);
        string byHand = $"git read-tree {snapshot} && git commit --allow-empty --no-verify -m \"{subject}\" -m \"{trailer}\"";
        if (branch is null || head != started)
        {
            return new RedCommitOutcome(null,
                $"HEAD moved while the red ran (it was {Describe(started)}, it is {Describe(head)}{(branch is null ? ", detached" : string.Empty)}), so the red is not committed. "
                + $"To commit the tree this red judged, check out the branch at {Describe(started)} again and run: {byHand} (your staged changes become unstaged; no file changes)");
        }

        string index = Path.Combine(Path.GetTempPath(), $"lockflight-red-index-{Guid.CreateVersion7()}");
        try
        {
            Dictionary<string, string> privateIndex = new(StringComparer.Ordinal) { [SnapshotGit.IndexVariable] = index };
            if ((await SnapshotGit.RunAsync(["read-tree", $"{snapshot}^{{tree}}"], root, timeLimit, privateIndex, cancellationToken)).ExitCode != 0)
            {
                return new RedCommitOutcome(null, "git could not read the red-time tree into an index; commit the red yourself: " + byHand);
            }

            ProcessResult commit = await SnapshotGit.RunAsync(["commit", "-q", "--allow-empty", "-m", subject, "-m", trailer], root, timeLimit, privateIndex, cancellationToken);
            if (commit.ExitCode != 0)
            {
                return new RedCommitOutcome(null,
                    $"git commit refused the red commit: {commit.Combined.Trim()}. A pre-commit hook that runs the tests refuses a red by nature, and a red commit "
                    + $"fails its tests on purpose, so you may commit the tree this red judged without hooks: {byHand} (your staged changes become unstaged; no file changes)");
            }

            string committed = (await SnapshotGit.OutputAsync(["rev-parse", "HEAD"], root, timeLimit, cancellationToken)) ?? string.Empty;
            string? parent = await SnapshotGit.OutputAsync(["rev-parse", "--verify", "--quiet", "HEAD^1"], root, timeLimit, cancellationToken);
            if (parent != started || await SnapshotGit.OutputAsync(["symbolic-ref", "-q", "HEAD"], root, timeLimit, cancellationToken) != branch)
            {
                return new RedCommitOutcome(committed,
                    $"Red commit {RedGreenSession.Short(committed)} was made, but HEAD moved while it was made: its parent is {Describe(parent)}, not {Describe(started)}. Check it before you push.");
            }

            string indexNote = await KeepIndexAsync(root, timeLimit, cancellationToken)
                ? string.Empty
                : " git could not reset your index to the red commit for its paths, so they show as staged changes against it: run git reset -q before you commit again.";
            ProcessResult committedTree = await SnapshotGit.RunAsync(["rev-parse", "HEAD^{tree}"], root, timeLimit, cancellationToken);
            ProcessResult judgedTree = await SnapshotGit.RunAsync(["rev-parse", $"{snapshot}^{{tree}}"], root, timeLimit, cancellationToken);
            string note = committedTree.Output.Trim() == judgedTree.Output.Trim()
                ? $"Red commit {RedGreenSession.Short(committed)} ({subject}, {trailer}): the tree this red judged; CI re-runs the red there."
                : $"Red commit {RedGreenSession.Short(committed)} differs from the tree this red judged (a hook changed it); CI judges the commit as it is.";
            return new RedCommitOutcome(committed, note + indexNote);
        }
        finally
        {
            File.Delete(index);
        }
    }

    /// <summary>
    /// Resets the person's index to the new commit for the paths the red commit holds, keeping everything else staged; false when git fails. The paths
    /// are the repository's (<c>top</c> magic), so a project root below the top level resets the right ones; a first commit has
    /// no parent to compare with (<c>--root</c>).
    /// </summary>
    private static async Task<bool> KeepIndexAsync(string root, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        ProcessResult changed = await SnapshotGit.RunAsync(["diff-tree", "--no-commit-id", "--root", "-r", "--name-only", "-z", "--no-renames", "HEAD"], root, timeLimit, cancellationToken);
        if (changed.ExitCode != 0)
        {
            return false;
        }

        foreach (string[] chunk in changed.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Chunk(PathsPerCall))
        {
            if ((await SnapshotGit.RunAsync(["reset", "-q", "HEAD", "--", .. chunk.Select(path => $":(top,literal){path}")], root, timeLimit, cancellationToken)).ExitCode != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string Describe(string? commit) => commit is null ? "no commit yet" : RedGreenSession.Short(commit);
}
