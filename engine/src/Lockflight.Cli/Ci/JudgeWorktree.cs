using Lockflight.Cli.Lifecycle.RedGreen.Snapshots;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The one detached worktree the judge builds every tree in, moved from commit to commit with <c>reset --hard</c> (no checkout
/// hooks) while <c>bin/</c> and <c>obj/</c> stay, so MSBuild rebuilds only what changed between the commits. Before each build the
/// base policy is laid over it, and empty <c>Directory.Build.props</c>, <c>Directory.Build.targets</c> and <c>global.json</c> stop
/// MSBuild's and the SDK's upward search at its root where the commit has none of its own. It lives outside the checkout (CI's
/// temporary folder) or, locally, in the repository's git directory; the checkout, its index and branch are never touched.
/// </summary>
internal sealed class JudgeWorktree
{
    private const string EmptyProject = "<Project>\n</Project>\n";
    private const string EmptyGlobalJson = "{\n}\n";

    private static readonly TimeSpan CleanupTimeLimit = TimeSpan.FromSeconds(60);
    private static readonly string[] Sentinels = ["Directory.Build.props", "Directory.Build.targets", "global.json"];
    private static readonly string[] KeptOutputs = ["-e", "bin/", "-e", "obj/"];

    private readonly string repositoryRoot;
    private readonly TimeSpan timeLimit;

    private JudgeWorktree(string repositoryRoot, string directory, TimeSpan timeLimit)
    {
        this.repositoryRoot = repositoryRoot;
        this.timeLimit = timeLimit;
        Root = directory;
    }

    /// <summary>The worktree's root, where the tree of the current commit is.</summary>
    public string Root { get; }

    /// <summary>The commit the worktree holds now; null before the first move.</summary>
    public string? Commit { get; private set; }

    /// <summary>Adds a worktree under <paramref name="parent"/>; null, after cleaning up, when git cannot.</summary>
    public static async Task<JudgeWorktree?> OpenAsync(string repositoryRoot, string parent, string firstCommit, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(parent);
        string directory = Path.Combine(parent, $"judge-{Guid.CreateVersion7()}");
        ProcessResult added = await SnapshotGit.RunAsync(["worktree", "add", "--detach", "--no-checkout", "--quiet", directory, firstCommit], repositoryRoot, timeLimit, cancellationToken);
        JudgeWorktree worktree = new(repositoryRoot, directory, timeLimit);
        if (added.ExitCode != 0)
        {
            await worktree.RemoveAsync();
            return null;
        }

        return worktree;
    }

    /// <summary>
    /// Moves the worktree to <paramref name="commit"/> and lays the policy of <paramref name="policyCommit"/> over it; with
    /// <paramref name="clean"/> the build output goes too (the retry after an incremental build failed). Null on success, else why.
    /// </summary>
    public async Task<string?> MoveToAsync(string commit, string policyCommit, bool clean, CancellationToken cancellationToken)
    {
        ProcessResult reset = await SnapshotGit.RunAsync(["reset", "--hard", "--quiet", commit], Root, timeLimit, cancellationToken);
        ProcessResult cleaned = reset.ExitCode == 0
            ? await SnapshotGit.RunAsync(["clean", "-ffdxq", .. clean ? [] : KeptOutputs], Root, timeLimit, cancellationToken)
            : reset;
        if (cleaned.ExitCode != 0)
        {
            return $"git could not check out {CommitIds.Short(commit)} in the judge's worktree: {cleaned.Combined.Trim()}";
        }

        Commit = commit;
        if (await PolicyOverlay.ApplyAsync(repositoryRoot, Root, commit, policyCommit, timeLimit, cancellationToken) is string notApplied)
        {
            return notApplied;
        }

        foreach (string sentinel in Sentinels.Where(name => !File.Exists(Path.Combine(Root, name))))
        {
            await File.WriteAllTextAsync(Path.Combine(Root, sentinel), sentinel == "global.json" ? EmptyGlobalJson : EmptyProject, cancellationToken);
        }

        return null;
    }

    /// <summary>Removes the worktree and git's record of it; runs after a cancelled run too, each git call bounded on its own.</summary>
    public async Task RemoveAsync()
    {
        await SnapshotGit.RunAsync(["worktree", "remove", "--force", Root], repositoryRoot, CleanupTimeLimit, CancellationToken.None);
        try
        {
            if (Directory.Exists(Root))
            {
                foreach (FileInfo file in new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    file.Attributes = FileAttributes.Normal;
                }

                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A build server can hold a file a moment longer; git's record is pruned below either way.
        }

        await SnapshotGit.RunAsync(["worktree", "prune"], repositoryRoot, CleanupTimeLimit, CancellationToken.None);
    }
}
