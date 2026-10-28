namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// The branch checked out in the git repository holding a directory, read from <c>HEAD</c> without running git: the directory or
/// the nearest parent with a <c>.git</c> directory, or a <c>.git</c> file (<c>gitdir: …</c>) as a linked worktree or submodule has.
/// </summary>
internal static class GitBranch
{
    private const string GitEntry = ".git";
    private const string HeadFile = "HEAD";
    private const string GitDirectoryPrefix = "gitdir:";
    private const string BranchRefPrefix = "ref: refs/heads/";

    /// <summary>The branch name (<c>feat/006-weak-spots</c>); null outside a repository, on a detached HEAD, or when HEAD is unreadable.</summary>
    public static string? Current(string directory)
    {
        for (DirectoryInfo? current = new(Path.GetFullPath(directory)); current is not null; current = current.Parent)
        {
            string entry = Path.Combine(current.FullName, GitEntry);
            if (Directory.Exists(entry))
            {
                return BranchIn(Path.Combine(entry, HeadFile));
            }

            if (File.Exists(entry))
            {
                return GitDirectoryOf(entry, current.FullName) is string gitDirectory ? BranchIn(Path.Combine(gitDirectory, HeadFile)) : null;
            }
        }

        return null;
    }

    private static string? GitDirectoryOf(string gitFile, string workingTree)
    {
        string? line = FirstLine(gitFile);
        return line is not null && line.StartsWith(GitDirectoryPrefix, StringComparison.Ordinal)
            ? Path.GetFullPath(line[GitDirectoryPrefix.Length..].Trim(), workingTree)
            : null;
    }

    private static string? BranchIn(string headFile) =>
        FirstLine(headFile) is string head && head.StartsWith(BranchRefPrefix, StringComparison.Ordinal) && head.Length > BranchRefPrefix.Length
            ? head[BranchRefPrefix.Length..]
            : null;

    private static string? FirstLine(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadLines(path).FirstOrDefault()?.Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
