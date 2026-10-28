using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Docs;

/// <summary>
/// The paths HEAD's first-parent history deleted or renamed, newest removal first, and the directories that went with them. Read
/// once per scan with a cap on commits and time: a deep history makes the check find less, never more.
/// </summary>
internal sealed class PathHistory(IReadOnlyDictionary<string, PathRemoval> removals, IReadOnlyList<string> notes)
{
    private const int MaxCommits = 5000;

    private const char CommitMarker = '\u0001';

    private const int MaxRenameHops = 20;

    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, PathRemoval> removedDirectories = DirectoriesOf(removals);

    private readonly Dictionary<string, string> removedByName = NamesOf(removals);

    /// <summary>What the history read could not cover (shallow clone, time limit), for the report.</summary>
    public IReadOnlyList<string> Notes => notes;

    /// <summary>How <paramref name="path"/> left the repository, following renames to the newest name (or to the deletion that ended them); null when history never removed it.</summary>
    public PathRemoval? Removal(string path)
    {
        if (!removals.TryGetValue(path, out PathRemoval? removal) && !removedDirectories.TryGetValue(path, out removal))
        {
            return null;
        }

        for (int hop = 0; hop < MaxRenameHops && removal.RenamedTo is string next && removals.TryGetValue(next, out PathRemoval? later); hop++)
        {
            removal = later;
        }

        return removal;
    }

    /// <summary>The newest removed path named <paramref name="fileName"/> and how it went; null when history removed no such file.</summary>
    public (string Path, PathRemoval Removal)? RemovedNamed(string fileName) =>
        removedByName.TryGetValue(fileName, out string? path) && Removal(path) is PathRemoval removal ? (path, removal) : null;

    public static async Task<PathHistory> LoadAsync(string root, CancellationToken cancellationToken)
    {
        List<string> notes = [];
        ProcessResult shallow = await ProcessRunner.RunAsync("git", ["rev-parse", "--is-shallow-repository"], root, cancellationToken);
        if (shallow.Output.Trim() == "true")
        {
            notes.Add("shallow clone: deleted and renamed paths are found only in the history this clone has (fetch it in full for the rest)");
        }

        ProcessResult log = await ProcessRunner.RunAsync(
            "git",
            ["-c", "core.quotePath=false", "log", "--first-parent", "--diff-filter=DR", "-M", "--name-status", $"--format={CommitMarker}%h", $"--max-count={MaxCommits}", "HEAD", "--"],
            root, TimeLimit, cancellationToken);
        if (log.TimedOut)
        {
            notes.Add($"history read stopped after {TimeLimit.TotalSeconds:0} s: deleted and renamed paths were not checked");
            return new PathHistory(new Dictionary<string, PathRemoval>(), notes);
        }

        return new PathHistory(log.ExitCode == 0 ? Parse(log.Output) : new Dictionary<string, PathRemoval>(), notes);
    }

    private static Dictionary<string, PathRemoval> Parse(string output)
    {
        Dictionary<string, PathRemoval> removals = new(StringComparer.Ordinal);
        string commit = string.Empty;
        foreach (string line in output.Split('\n'))
        {
            if (line.StartsWith(CommitMarker))
            {
                commit = line[1..].Trim();
                continue;
            }

            string[] fields = line.Split('\t');
            if (fields is [string status, string path] && status.StartsWith('D'))
            {
                removals.TryAdd(path, new PathRemoval(commit, null));
            }
            else if (fields is [string renameStatus, string from, string to] && renameStatus.StartsWith('R'))
            {
                removals.TryAdd(from, new PathRemoval(commit, to));
            }
        }

        return removals;
    }

    /// <summary>Removed paths by file name, newest first: the parse keeps git's newest-first order.</summary>
    private static Dictionary<string, string> NamesOf(IReadOnlyDictionary<string, PathRemoval> removals)
    {
        Dictionary<string, string> names = new(StringComparer.Ordinal);
        foreach (string path in removals.Keys)
        {
            names.TryAdd(RepoPath.FileName(path), path);
        }

        return names;
    }

    private static Dictionary<string, PathRemoval> DirectoriesOf(IReadOnlyDictionary<string, PathRemoval> removals)
    {
        Dictionary<string, PathRemoval> directories = new(StringComparer.Ordinal);
        foreach ((string path, PathRemoval removal) in removals)
        {
            foreach (string directory in RepoPath.SelfAndAncestors(RepoPath.Directory(path)).TakeWhile(directory => directory.Length > 0))
            {
                directories.TryAdd(directory, new PathRemoval(removal.Commit, null));
            }
        }

        return directories;
    }
}
