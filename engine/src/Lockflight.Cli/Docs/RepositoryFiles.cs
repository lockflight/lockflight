using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Docs;

/// <summary>
/// The files of a git working tree as the drift check sees them: <see cref="Tracked"/> (what is scanned) and the present files
/// (tracked plus untracked files that are not ignored: what a reference may resolve to, so a file the agent just wrote counts).
/// </summary>
internal sealed class RepositoryFiles(string root, IReadOnlyList<string> tracked, IReadOnlySet<string> present)
{
    private const string Git = "git";

    private readonly HashSet<string> directories = [.. present.SelectMany(path => RepoPath.SelfAndAncestors(RepoPath.Directory(path)))];

    private readonly ILookup<string, string> byName = present.ToLookup(RepoPath.FileName, StringComparer.Ordinal);

    /// <summary>The directory scanned, inside a git working tree; every path is relative to it.</summary>
    public string Root => root;

    public IReadOnlyList<string> Tracked => tracked;

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

    private static string[] Split(string output) => output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
}
