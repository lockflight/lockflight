using System.Text;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Routing;

/// <summary>
/// The git calls routing makes, with flags pinned against the user's configuration so a local run and CI count the same lines:
/// unquoted names, fixed <c>a/</c> <c>b/</c> prefixes, paths relative to the directory git runs in, renames as a delete plus an
/// add, Myers diff, no external diff drivers or text conversion, and submodules always compared.
/// </summary>
internal static class RoutingGit
{
    private const string Executable = "git";
    private const string ZeroId = "0000000000000000000000000000000000000000";

    private static readonly TimeSpan TimeLimit = TimeSpan.FromMinutes(5);

    private static readonly string[] Pinned =
    [
        "-c", "core.quotePath=false", "-c", "diff.noprefix=false", "-c", "diff.mnemonicPrefix=false", "-c", "diff.relative=false",
    ];

    private static readonly string[] DiffFlags =
    [
        "--no-color", "--no-ext-diff", "--no-textconv", "--no-renames", "--diff-algorithm=myers", "--indent-heuristic",
        "--ignore-submodules=none", "--relative", "--src-prefix=a/", "--dst-prefix=b/",
    ];

    /// <summary>Whether <paramref name="id"/> is git's all-zero id (a push without a previous commit, a deleted ref).</summary>
    public static bool IsZeroId(string id) => id.Length >= ZeroId.Length && id.All(character => character == '0');

    /// <summary>The newest common ancestor of two commits; null when there is none (unrelated histories, a shallow clone) or git fails.</summary>
    public static async Task<string?> MergeBaseAsync(string directory, string first, string second, CancellationToken cancellationToken)
    {
        ProcessResult mergeBase = await RunAsync(directory, ["merge-base", "--end-of-options", first, second], cancellationToken);
        return mergeBase.ExitCode == 0 && mergeBase.Output.Trim() is { Length: > 0 } id ? id : null;
    }

    public static async Task<bool> IsShallowAsync(string directory, CancellationToken cancellationToken) =>
        (await RunAsync(directory, ["rev-parse", "--is-shallow-repository"], cancellationToken)).Output.Trim() == "true";

    /// <summary>The directory's path inside its repository (<c>src/</c>, or empty at the top); null when git fails.</summary>
    public static async Task<string?> PrefixAsync(string directory, CancellationToken cancellationToken)
    {
        ProcessResult prefix = await RunAsync(directory, ["rev-parse", "--show-prefix"], cancellationToken);
        return prefix.ExitCode == 0 ? prefix.Output.Trim() : null;
    }

    /// <summary>
    /// A diff of <paramref name="range"/> (one commit: against the working tree; two: between them) in the given output format
    /// (<c>--raw -z --no-abbrev</c>, <c>--numstat -z</c> or <c>-U0</c>); null when git fails.
    /// </summary>
    public static async Task<string?> DiffAsync(string directory, IReadOnlyList<string> range, IReadOnlyList<string> format, CancellationToken cancellationToken)
    {
        ProcessResult diff = await RunAsync(directory, ["diff", .. DiffFlags, .. format, .. range, "--"], cancellationToken);
        return diff.ExitCode == 0 ? diff.Output : null;
    }

    /// <summary>Every path in <paramref name="commit"/>'s tree under the directory, relative to it; null when git fails.</summary>
    public static async Task<IReadOnlyList<string>?> TreePathsAsync(string directory, string commit, CancellationToken cancellationToken)
    {
        ProcessResult tree = await RunAsync(directory, ["ls-tree", "-r", "-z", "--name-only", commit], cancellationToken);
        return tree.ExitCode == 0 ? Split(tree.Output) : null;
    }

    /// <summary>Tracked and untracked, not-ignored paths of the working tree under the directory; null when git fails.</summary>
    public static async Task<IReadOnlyList<string>?> WorkingTreePathsAsync(string directory, CancellationToken cancellationToken)
    {
        ProcessResult listing = await RunAsync(directory, ["ls-files", "-z", "--cached", "--others", "--exclude-standard"], cancellationToken);
        return listing.ExitCode == 0 ? Split(listing.Output) : null;
    }

    /// <summary>Untracked, not-ignored files under the directory; null when git fails.</summary>
    public static async Task<IReadOnlyList<string>?> UntrackedPathsAsync(string directory, CancellationToken cancellationToken)
    {
        ProcessResult listing = await RunAsync(directory, ["ls-files", "-z", "--others", "--exclude-standard"], cancellationToken);
        return listing.ExitCode == 0 ? Split(listing.Output) : null;
    }

    /// <summary>
    /// The blob ids git would store for working-tree files (with the repository's clean filters), keyed by path; null when git
    /// fails. A path with a line break cannot be asked and is left out.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>?> WorkingTreeBlobsAsync(
        string directory, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        string[] askable = [.. paths.Where(path => !path.Contains('\n', StringComparison.Ordinal))];
        Dictionary<string, string> blobs = new(StringComparer.Ordinal);
        if (askable.Length == 0)
        {
            return blobs;
        }

        BinaryProcessResult hashes = await ProcessRunner.RunForBytesAsync(
            Executable, [.. Pinned, "hash-object", "--stdin-paths"], directory, Encoding.UTF8.GetBytes(string.Join('\n', askable) + "\n"), TimeLimit, cancellationToken);
        if (hashes.ExitCode != 0)
        {
            return null;
        }

        string[] ids = Encoding.UTF8.GetString(hashes.Output).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length != askable.Length)
        {
            return null;
        }

        for (int index = 0; index < askable.Length; index++)
        {
            blobs[askable[index]] = ids[index].Trim();
        }

        return blobs;
    }

    /// <summary>
    /// The contents of <c>&lt;commit&gt;:&lt;path&gt;</c> for each path (relative to the repository root), read with one
    /// <c>git cat-file --batch</c>; a missing path or one that is not a blob is left out. Null when git fails.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, byte[]>?> BlobsAsync(
        string directory, string commit, IReadOnlyList<string> repositoryPaths, CancellationToken cancellationToken)
    {
        string[] askable = [.. repositoryPaths.Where(path => !path.Contains('\n', StringComparison.Ordinal) && !path.Contains('\r', StringComparison.Ordinal))];
        if (askable.Length == 0)
        {
            return new Dictionary<string, byte[]>(StringComparer.Ordinal);
        }

        byte[] requests = Encoding.UTF8.GetBytes(string.Concat(askable.Select(path => $"{commit}:{path}\n")));
        BinaryProcessResult batch = await ProcessRunner.RunForBytesAsync(Executable, [.. Pinned, "cat-file", "--batch"], directory, requests, TimeLimit, cancellationToken);
        return batch.ExitCode == 0 ? CatFileBatch.Parse(batch.Output, askable) : null;
    }

    private static Task<ProcessResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        ProcessRunner.RunAsync(Executable, [.. Pinned, .. arguments], directory, TimeLimit, cancellationToken);

    private static string[] Split(string nulSeparated) => nulSeparated.Split('\0', StringSplitOptions.RemoveEmptyEntries);
}
