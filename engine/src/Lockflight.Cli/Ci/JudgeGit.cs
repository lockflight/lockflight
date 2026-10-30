using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The git queries of the CI judge, on full commit ids only, with output independent of the user's git configuration (unquoted
/// names, NUL-separated lists, no renames, no external diff drivers or text conversion, no signature checks in logs).
/// </summary>
internal static class JudgeGit
{
    private const string Executable = "git";

    private static readonly string[] Pinned = ["-c", "core.quotePath=false", "-c", "log.showSignature=false"];

    /// <summary>Whether <paramref name="commit"/> names a commit object this clone has.</summary>
    public static async Task<bool> HasCommitAsync(string root, string commit, CancellationToken cancellationToken) =>
        (await ProcessRunner.RunAsync(Executable, ["cat-file", "-e", $"{commit}^{{commit}}"], root, cancellationToken)).ExitCode == 0;

    /// <summary>The full id of the commit <paramref name="reference"/> names; null when it names none.</summary>
    public static Task<string?> ResolveAsync(string root, string reference, CancellationToken cancellationToken) =>
        Git.CommitOfAsync(root, reference, cancellationToken);

    /// <summary>Whether this clone is shallow (some history is missing).</summary>
    public static async Task<bool> IsShallowAsync(string root, CancellationToken cancellationToken)
    {
        ProcessResult shallow = await ProcessRunner.RunAsync(Executable, ["rev-parse", "--is-shallow-repository"], root, cancellationToken);
        return shallow.ExitCode == 0 && shallow.Output.Trim() == "true";
    }

    /// <summary>
    /// The merge bases of two commits, the one <c>git merge-base</c> prints first; empty when they share no history (or git
    /// cannot tell, as in a shallow clone).
    /// </summary>
    public static async Task<IReadOnlyList<string>> MergeBasesAsync(string root, string left, string right, CancellationToken cancellationToken)
    {
        ProcessResult first = await ProcessRunner.RunAsync(Executable, ["merge-base", left, right], root, cancellationToken);
        if (first.ExitCode != 0 || first.Output.Trim() is not { Length: > 0 } chosen)
        {
            return [];
        }

        ProcessResult all = await ProcessRunner.RunAsync(Executable, ["merge-base", "--all", left, right], root, cancellationToken);
        string[] others = all.ExitCode == 0 ? [.. Lines(all.Output).Where(commit => commit != chosen)] : [];
        return [chosen, .. others];
    }

    /// <summary>
    /// The commits in <c>from..to</c> in topological order, oldest first, with their red and amend trailers and subject; null when
    /// git fails or its output does not have the expected shape.
    /// </summary>
    public static async Task<IReadOnlyList<CommitTrailers>?> HistoryAsync(string root, string from, string to, CancellationToken cancellationToken)
    {
        // Commit messages cannot hold NUL, so NUL ends every field; each commit has exactly the fields of the format.
        const string Format = "%H%x00%P%x00%cI%x00%(trailers:key=Lockflight-Red,valueonly,separator=%x01)%x00%(trailers:key=Lockflight-Amend,valueonly,separator=%x01)%x00%s";
        ProcessResult log = await ProcessRunner.RunAsync(
            Executable, [.. Pinned, "log", "-z", "--no-color", "--topo-order", "--reverse", $"--format={Format}", $"{from}..{to}", "--"], root, cancellationToken);
        return log.ExitCode == 0 ? CommitTrailers.ParseLog(log.Output) : null;
    }

    /// <summary>The text of <paramref name="path"/> at <paramref name="commit"/>; null when the commit has no such file.</summary>
    public static async Task<string?> ShowFileAsync(string root, string commit, string path, CancellationToken cancellationToken)
    {
        ProcessResult show = await ProcessRunner.RunAsync(Executable, [.. Pinned, "show", "--no-textconv", $"{commit}:{path}"], root, cancellationToken);
        return show.ExitCode == 0 ? show.Output : null;
    }

    /// <summary>The blob id of <paramref name="path"/> at <paramref name="commit"/>; null when there is none.</summary>
    public static async Task<string?> BlobOfAsync(string root, string commit, string path, CancellationToken cancellationToken)
    {
        ProcessResult blob = await ProcessRunner.RunAsync(Executable, ["rev-parse", "--verify", "--quiet", $"{commit}:{path}"], root, cancellationToken);
        return blob.ExitCode == 0 && blob.Output.Trim() is { Length: > 0 } id ? id : null;
    }

    /// <summary>Every file of the tree at <paramref name="commit"/>, '/'-separated; null when git fails.</summary>
    public static async Task<IReadOnlyList<string>?> TreeFilesAsync(string root, string commit, CancellationToken cancellationToken)
    {
        ProcessResult tree = await ProcessRunner.RunAsync(Executable, [.. Pinned, "ls-tree", "-r", "-z", "--name-only", commit], root, cancellationToken);
        return tree.ExitCode == 0 ? Split(tree.Output) : null;
    }

    /// <summary>The files that differ between two commits, both sides of a rename; null when git fails.</summary>
    public static async Task<IReadOnlyList<string>?> ChangedFilesAsync(string root, string from, string to, CancellationToken cancellationToken)
    {
        ProcessResult diff = await ProcessRunner.RunAsync(
            Executable, [.. Pinned, "diff", "--name-only", "-z", "--no-renames", "--no-ext-diff", "--no-textconv", "--ignore-submodules=none", from, to, "--"], root, cancellationToken);
        return diff.ExitCode == 0 ? Split(diff.Output) : null;
    }

    /// <summary>The zero-context diff of <paramref name="paths"/> between two commits; null when git fails.</summary>
    public static async Task<string?> DiffHunksAsync(string root, string from, string to, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        ProcessResult diff = await ProcessRunner.RunAsync(
            Executable,
            [.. Pinned, "diff", "-U0", "--no-color", "--no-ext-diff", "--no-textconv", "--no-renames", "--diff-algorithm=myers", "--src-prefix=a/", "--dst-prefix=b/", from, to, "--", .. paths],
            root,
            cancellationToken);
        return diff.ExitCode == 0 ? diff.Output : null;
    }

    /// <summary>The diff of <paramref name="paths"/> between two commits with three lines of context, as a person reads it; null when git fails.</summary>
    public static async Task<string?> DiffAsync(string root, string from, string to, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        ProcessResult diff = await ProcessRunner.RunAsync(
            Executable,
            [.. Pinned, "diff", "--no-color", "--no-ext-diff", "--no-textconv", "--find-renames", "--src-prefix=a/", "--dst-prefix=b/", from, to, "--", .. paths],
            root,
            cancellationToken);
        return diff.ExitCode == 0 ? diff.Output : null;
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] Split(string nulSeparated) => nulSeparated.Split('\0', StringSplitOptions.RemoveEmptyEntries);
}
