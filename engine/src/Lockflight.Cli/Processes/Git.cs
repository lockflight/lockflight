namespace Lockflight.Cli.Processes;

/// <summary>
/// The git queries several commands share, with flags that make the output independent of the user's git configuration:
/// unquoted names (<c>core.quotePath=false</c>, or NUL-separated), paths relative to the directory git runs in (<c>--relative</c>,
/// so they join onto it even when it is a subdirectory of the repository), renames as a delete plus an add (<c>--no-renames</c>,
/// so the old path counts as changed too), no external diff drivers and fixed <c>a/</c> <c>b/</c> prefixes.
/// </summary>
internal static class Git
{
    /// <summary>What evidence records instead of a commit when there is none (no repository, no commit yet).</summary>
    public const string UnknownCommit = "unknown";

    /// <summary>The range of uncommitted changes (staged and unstaged) of tracked files.</summary>
    public const string WorkingTree = "HEAD";

    private const string Executable = "git";
    private const string RemotesPrefix = "refs/remotes/";

    private static readonly string[] Unquoted = ["-c", "core.quotePath=false"];

    /// <summary>The changes of a branch since it left <paramref name="baseRef"/> (the merge base), as CI compares a pull request.</summary>
    public static string SinceMergeBase(string baseRef) => $"{baseRef}...HEAD";

    /// <summary>The commit HEAD points at; null outside a repository or before the first commit.</summary>
    public static async Task<string?> HeadAsync(string root, CancellationToken cancellationToken)
    {
        ProcessResult head = await ProcessRunner.RunAsync(Executable, ["rev-parse", "HEAD"], root, cancellationToken);
        return head.ExitCode == 0 ? head.Output.Trim() : null;
    }

    /// <summary>The commit <paramref name="reference"/> names (a branch, tag or commit); null when it names none.</summary>
    public static async Task<string?> CommitOfAsync(string root, string reference, CancellationToken cancellationToken)
    {
        ProcessResult commit = await ProcessRunner.RunAsync(Executable, ["rev-parse", "--verify", "--quiet", "--end-of-options", $"{reference}^{{commit}}"], root, cancellationToken);
        return commit.ExitCode == 0 && commit.Output.Trim() is { Length: > 0 } id ? id : null;
    }

    /// <summary>
    /// The repository's default branch as this clone knows it: the branch <c>origin/HEAD</c> points at (<c>origin/main</c>), else
    /// <paramref name="fallback"/>.
    /// </summary>
    public static async Task<string> DefaultBranchAsync(string root, string fallback, CancellationToken cancellationToken)
    {
        ProcessResult head = await ProcessRunner.RunAsync(Executable, ["symbolic-ref", "--quiet", "refs/remotes/origin/HEAD"], root, cancellationToken);
        return head.ExitCode == 0 && head.Output.Trim() is { } reference && reference.StartsWith(RemotesPrefix, StringComparison.Ordinal)
            ? reference[RemotesPrefix.Length..]
            : fallback;
    }

    /// <summary>The newest commit both <paramref name="reference"/> and HEAD descend from; null when there is none or git fails.</summary>
    public static async Task<string?> MergeBaseAsync(string root, string reference, CancellationToken cancellationToken)
    {
        ProcessResult mergeBase = await ProcessRunner.RunAsync(Executable, ["merge-base", "--end-of-options", reference, "HEAD"], root, cancellationToken);
        return mergeBase.ExitCode == 0 && mergeBase.Output.Trim() is { Length: > 0 } id ? id : null;
    }

    /// <summary>
    /// What uncommitted changes are compared with: <see cref="WorkingTree"/> (HEAD), or — before the first commit — git's empty
    /// tree, so every tracked file counts as added. Null when git cannot name either (not a repository).
    /// </summary>
    public static async Task<string?> UncommittedBaseAsync(string root, CancellationToken cancellationToken)
    {
        if (await HeadAsync(root, cancellationToken) is not null)
        {
            return WorkingTree;
        }

        if ((await ProcessRunner.RunAsync(Executable, ["rev-parse", "--git-dir"], root, cancellationToken)).ExitCode != 0)
        {
            return null;
        }

        // The empty tree's id depends on the repository's hash algorithm (SHA-1 or SHA-256), so git computes it.
        string empty = Path.Combine(Path.GetTempPath(), $"lockflight-empty-{Guid.CreateVersion7()}");
        try
        {
            await File.WriteAllBytesAsync(empty, [], cancellationToken);
            ProcessResult tree = await ProcessRunner.RunAsync(Executable, ["hash-object", "-t", "tree", empty], root, cancellationToken);
            return tree.ExitCode == 0 && tree.Output.Trim() is { Length: > 0 } id ? id : null;
        }
        finally
        {
            File.Delete(empty);
        }
    }

    /// <summary>A git configuration value; null when it is unset or empty.</summary>
    public static async Task<string?> ConfigValueAsync(string root, string key, CancellationToken cancellationToken)
    {
        ProcessResult value = await ProcessRunner.RunAsync(Executable, ["config", "--get", key], root, cancellationToken);
        return value.ExitCode == 0 && value.Output.Trim() is { Length: > 0 } text ? text : null;
    }

    /// <summary>
    /// Untracked files that are not ignored (<c>.gitignore</c>, <c>.git/info/exclude</c>, the global excludes) under
    /// <paramref name="root"/>, relative to it and '/'-separated, optionally limited to <paramref name="pathspecs"/>; null when git
    /// fails (e.g. outside a repository). NUL-separated output keeps unusual names intact.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> UntrackedFilesAsync(
        string root, IReadOnlyList<string> pathspecs, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        ProcessResult listing = await ProcessRunner.RunAsync(
            Executable, ["ls-files", "--others", "--exclude-standard", "-z", "--", .. pathspecs], root, timeLimit, cancellationToken);
        return listing.ExitCode == 0 ? Split(listing.Output) : null;
    }

    /// <summary>Every untracked, not-ignored file under <paramref name="root"/>; null when git fails.</summary>
    public static Task<IReadOnlyList<string>?> UntrackedFilesAsync(string root, CancellationToken cancellationToken) =>
        UntrackedFilesAsync(root, [], Timeout.InfiniteTimeSpan, cancellationToken);

    /// <summary>
    /// Files a range changes (<see cref="WorkingTree"/> or <see cref="SinceMergeBase"/>) under <paramref name="root"/>, relative to it
    /// and '/'-separated; a rename lists both paths. Null when git fails (unknown ref, shallow checkout, not a repository).
    /// </summary>
    public static async Task<IReadOnlyList<string>?> ChangedNamesAsync(string root, string range, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        ProcessResult diff = await ProcessRunner.RunAsync(
            Executable, [.. Unquoted, "diff", "--name-only", "-z", "--no-renames", "--no-ext-diff", "--relative", range, "--"], root, timeLimit, cancellationToken);
        return diff.ExitCode == 0 ? Split(diff.Output) : null;
    }

    /// <summary>
    /// The zero-context diff of a range under <paramref name="root"/>, for <see cref="UnifiedDiff"/>; null when git fails.
    /// </summary>
    public static Task<string?> DiffHunksAsync(
        string root, string range, IReadOnlyList<string> pathspecs, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        DiffHunksAsync(root, range, pathspecs, string.Empty, timeLimit, cancellationToken);

    /// <summary>
    /// The zero-context diff of a range with <paramref name="pathPrefix"/> (a child repository's unit path) written into the file
    /// names, so they read relative to the workspace root; null when git fails.
    /// </summary>
    public static async Task<string?> DiffHunksAsync(
        string root, string range, IReadOnlyList<string> pathspecs, string pathPrefix, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        ProcessResult diff = await ProcessRunner.RunAsync(
            Executable,
            [.. Unquoted, "diff", "-U0", "--no-color", "--no-ext-diff", "--no-renames", "--relative", $"--src-prefix=a/{pathPrefix}", $"--dst-prefix=b/{pathPrefix}", range, "--", .. pathspecs],
            root,
            timeLimit,
            cancellationToken);
        return diff.ExitCode == 0 ? diff.Output : null;
    }

    private static string[] Split(string nulSeparated) =>
        [.. nulSeparated.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(path => path.Trim().Length > 0)];
}
