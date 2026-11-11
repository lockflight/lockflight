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

    private const int PathsPerCall = 200;

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
    /// The worktree at <paramref name="directory"/>, kept between runs so its <c>bin/</c> and <c>obj/</c> make the next build
    /// incremental: reused when the directory is the top of a worktree that holds <paramref name="firstCommit"/>, else (re)added
    /// there; null, after cleaning up, when git cannot. The caller holds a lock so no two runs share it.
    /// </summary>
    public static async Task<JudgeWorktree?> OpenKeptAsync(string repositoryRoot, string directory, string firstCommit, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        JudgeWorktree worktree = new(repositoryRoot, directory, timeLimit);
        if (await IsKeptWorktreeAsync(directory, firstCommit, timeLimit, cancellationToken))
        {
            return worktree;
        }

        await worktree.RemoveAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        ProcessResult added = await SnapshotGit.RunAsync(["worktree", "add", "--detach", "--no-checkout", "--quiet", directory, firstCommit], repositoryRoot, timeLimit, cancellationToken);
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

    /// <summary>
    /// Lays <paramref name="paths"/> (file names, never patterns) as they are at <paramref name="fromCommit"/> over the tree the
    /// worktree holds, removes <paramref name="deleted"/>, then the base policy again (a test project's own style file must not
    /// override it). Null on success, else why.
    /// </summary>
    public async Task<string?> OverlayAsync(string fromCommit, IReadOnlyList<string> paths, IReadOnlyList<string> deleted, string policyCommit, CancellationToken cancellationToken)
    {
        foreach (string[] chunk in paths.Chunk(PathsPerCall))
        {
            ProcessResult checkout = await SnapshotGit.RunAsync(["--literal-pathspecs", "checkout", fromCommit, "--", .. chunk], Root, timeLimit, cancellationToken);
            if (checkout.ExitCode != 0)
            {
                return $"git could not lay {CommitIds.Short(fromCommit)}'s test files over {CommitIds.Short(Commit ?? fromCommit)}: {checkout.Combined.Trim()}";
            }
        }

        foreach (string path in deleted)
        {
            string file = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        return await PolicyOverlay.ApplyAsync(repositoryRoot, Root, Commit ?? fromCommit, policyCommit, timeLimit, cancellationToken);
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

    /// <summary>
    /// Whether <paramref name="directory"/> is the top of a git worktree (its own <c>.git</c> file, no prefix below a work tree)
    /// whose repository has <paramref name="commit"/>; never a directory inside some other checkout.
    /// </summary>
    private static async Task<bool> IsKeptWorktreeAsync(string directory, string commit, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        File.Exists(Path.Combine(directory, ".git"))
        && await SnapshotGit.RunAsync(["rev-parse", "--show-prefix"], directory, timeLimit, cancellationToken) is { ExitCode: 0 } prefix
        && prefix.Output.Trim().Length == 0
        && (await SnapshotGit.RunAsync(["cat-file", "-e", $"{commit}^{{commit}}"], directory, timeLimit, cancellationToken)).ExitCode == 0;
}
