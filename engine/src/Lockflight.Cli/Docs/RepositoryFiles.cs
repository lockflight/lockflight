using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Docs;

/// <summary>
/// The files of a git working tree as the drift check sees them: <see cref="Tracked"/> (what is scanned) and the present files
/// (tracked plus untracked files that are not ignored: what a reference may resolve to, so a file the agent just wrote counts).
/// <see cref="AtCommitAsync"/> gives the same view of a commit instead, without a checkout: its tree's files, its Markdown read
/// through git, and its history.
/// </summary>
internal sealed class RepositoryFiles(string root, IReadOnlyList<string> tracked, IReadOnlySet<string> present)
{
    private const string Git = "git";

    private const string Markdown = ".md";

    /// <summary>The Markdown files of a commit read at most this many at a time.</summary>
    private const int MaxParallelReads = 8;

    private IReadOnlyDictionary<string, string>? committedTexts;

    private readonly HashSet<string> directories = [.. present.SelectMany(path => RepoPath.SelfAndAncestors(RepoPath.Directory(path)))];

    private readonly ILookup<string, string> byName = present.ToLookup(RepoPath.FileName, StringComparer.Ordinal);

    /// <summary>The directory scanned, inside a git working tree; every path is relative to it.</summary>
    public string Root => root;

    public IReadOnlyList<string> Tracked => tracked;

    /// <summary>Every file a reference may resolve to: in a working tree, the untracked files that are not ignored too.</summary>
    public IEnumerable<string> Present => present;

    /// <summary>The revision whose history says which paths were deleted or renamed: HEAD, or the commit this view is of.</summary>
    public string Revision { get; private init; } = "HEAD";

    /// <summary>A present file's text: from the working tree, or for a commit's view (Markdown only) as that commit has it.</summary>
    public string ReadText(string path) =>
        committedTexts is null ? File.ReadAllText(Path.Combine(root, path)) : committedTexts.GetValueOrDefault(path, string.Empty);

    public bool Exists(string path) => path.Length == 0 || present.Contains(path) || directories.Contains(path);

    public bool DirectoryExists(string path) => path.Length == 0 || directories.Contains(path);

    public bool IsFile(string path) => present.Contains(path);

    /// <summary>The present files named <paramref name="fileName"/>, in any directory.</summary>
    public IEnumerable<string> Named(string fileName) => byName[fileName];

    /// <summary>The files at and below <paramref name="directory"/>; null outside a git working tree or when git is missing.</summary>
    public static async Task<RepositoryFiles?> LoadAsync(string directory, CancellationToken cancellationToken)
    {
        try
        {
            ProcessResult inside = await ProcessRunner.RunAsync(Git, ["rev-parse", "--is-inside-work-tree"], directory, cancellationToken);
            if (inside.ExitCode != 0 || inside.Output.Trim() != "true")
            {
                return null;
            }

            // Paths are relative to the directory, and only files below it count: a project inside a larger repository is scanned alone.
            string root = Path.GetFullPath(directory);

            ProcessResult trackedFiles = await ProcessRunner.RunAsync(Git, ["-c", "core.quotePath=false", "ls-files", "-z"], root, cancellationToken);
            ProcessResult others = await ProcessRunner.RunAsync(Git, ["-c", "core.quotePath=false", "ls-files", "-z", "--others", "--exclude-standard"], root, cancellationToken);
            if (trackedFiles.ExitCode != 0 || others.ExitCode != 0)
            {
                return null;
            }

            string[] tracked = Split(trackedFiles.Output);
            // A tracked file deleted in the working tree is no longer there to resolve to.
            HashSet<string> present = [.. tracked.Where(path => File.Exists(Path.Combine(root, path))).Concat(Split(others.Output))];
            return new RepositoryFiles(root, [.. tracked.Where(present.Contains)], present);
        }
        catch (ExecutableNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// The files of <paramref name="commit"/> at and below <paramref name="directory"/>, relative to it, with every Markdown file's
    /// text read from git (no checkout); null when git cannot list or read them.
    /// </summary>
    public static async Task<RepositoryFiles?> AtCommitAsync(string directory, string commit, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(directory);
        ProcessResult listing = await ProcessRunner.RunAsync(Git, ["-c", "core.quotePath=false", "ls-tree", "-r", "--name-only", "-z", commit], root, timeLimit, cancellationToken);
        if (listing.ExitCode != 0)
        {
            return null;
        }

        string[] files = Split(listing.Output);
        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        using SemaphoreSlim slots = new(MaxParallelReads);
        string?[] read = await Task.WhenAll(files.Where(path => path.EndsWith(Markdown, StringComparison.OrdinalIgnoreCase)).Select(async path =>
        {
            await slots.WaitAsync(cancellationToken);
            try
            {
                ProcessResult show = await ProcessRunner.RunAsync(Git, ["show", $"{commit}:./{path}"], root, timeLimit, cancellationToken);
                return show.ExitCode == 0 ? $"{path}\0{show.Output}" : null;
            }
            finally
            {
                slots.Release();
            }
        }));
        if (read.Any(entry => entry is null))
        {
            return null;
        }

        foreach (string entry in read.OfType<string>())
        {
            int separator = entry.IndexOf('\0', StringComparison.Ordinal);
            texts[entry[..separator]] = entry[(separator + 1)..];
        }

        return new RepositoryFiles(root, files, files.ToHashSet(StringComparer.Ordinal)) { committedTexts = texts, Revision = commit };
    }

    private static string[] Split(string output) => output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
}
