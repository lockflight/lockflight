using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates;

/// <summary>
/// Added or modified lines per C# file, from <c>git diff -U0</c> (<see cref="WorkspaceGit.DiffHunksAsync"/>): uncommitted changes plus — with a base ref (CI) —
/// everything since the merge base, in every working tree of a workspace. New untracked files count with all their lines.
/// Paths are relative to the project root with unquoted names, whatever the user's git configuration. Null when git fails — never "no changes".
/// </summary>
internal static class ChangedLines
{
    private static readonly string[] CSharpFiles = ["*.cs"];

    public static async Task<IReadOnlyDictionary<string, IReadOnlySet<int>>?> FindAsync(string root, string? baseRef, CancellationToken cancellationToken)
    {
        Dictionary<string, HashSet<int>> lines = new(StringComparer.Ordinal);
        List<string> ranges = [Git.WorkingTree];
        if (baseRef is not null)
        {
            ranges.Add(Git.SinceMergeBase(baseRef));
        }

        foreach (string range in ranges)
        {
            if (await WorkspaceGit.DiffHunksAsync(root, range, CSharpFiles, Timeout.InfiniteTimeSpan, cancellationToken) is not string diff)
            {
                return null;
            }

            AddFromDiff(lines, diff);
        }

        if (await WorkspaceGit.UntrackedFilesAsync(root, CSharpFiles, Timeout.InfiniteTimeSpan, cancellationToken) is not IReadOnlyList<string> untracked)
        {
            return null;
        }

        foreach (string file in untracked)
        {
            string path = Path.Combine(root, file);
            int lineCount = File.Exists(path) ? File.ReadAllLines(path).Length : 0;
            Set(lines, file).UnionWith(Enumerable.Range(1, lineCount));
        }

        return lines.ToDictionary(entry => entry.Key, entry => (IReadOnlySet<int>)entry.Value, StringComparer.Ordinal);
    }

    /// <summary>Adds the lines a zero-context unified diff adds, per file; a file whose hunks only delete is listed with no lines.</summary>
    public static void AddFromDiff(Dictionary<string, HashSet<int>> lines, string diff)
    {
        foreach (FileDiff file in UnifiedDiff.FilesOf(diff))
        {
            Set(lines, file.File).UnionWith(file.Added.Select(line => line.Line));
        }
    }

    private static HashSet<int> Set(Dictionary<string, HashSet<int>> lines, string file)
    {
        if (!lines.TryGetValue(file, out HashSet<int>? set))
        {
            set = [];
            lines[file] = set;
        }

        return set;
    }
}
