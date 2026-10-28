namespace Lockflight.Cli.Paths;

/// <summary>
/// On Windows a <c>..</c> is collapsed before any link is followed: Win32 normalizes <c>out\..\x</c> to <c>x</c> by its text, so
/// after a link <c>out</c> to a folder elsewhere the write lands beside <c>out</c> in the project, not beside the link's target
/// as on POSIX. A POSIX layer (Git Bash) may resolve it either way, so a path with <c>..</c> is judged in both forms there.
/// </summary>
internal static class WindowsParentSegments
{
    private const string ParentSegment = "..";

    /// <summary>The paths, each followed on Windows by its collapsed spelling when it has a <c>..</c> segment.</summary>
    public static IReadOnlyList<string> WithCollapsed(IReadOnlyList<string> paths, string workingDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return paths;
        }

        List<string> forms = [];
        foreach (string path in paths)
        {
            forms.Add(path);
            if (HasParentSegment(path))
            {
                forms.Add(PathRules.ToAbsolute(path, workingDirectory));
            }
        }

        return forms;
    }

    public static bool HasParentSegment(string path) => path.Split('/', '\\').Contains(ParentSegment);
}
