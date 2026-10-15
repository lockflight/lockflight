namespace Lockflight.Cli.Paths;

/// <summary>
/// Checks a directory the engine writes its own state to (proposals, waiting approval codes) before it writes there. The guard
/// keeps the agent out of <c>.lockflight/</c>, but the engine does not rely on that alone: a part of the path that is a symbolic
/// link would send the write wherever it points, perhaps into a protected path, and a file in its place would fail the write.
/// </summary>
internal static class ProjectDirectories
{
    /// <summary>
    /// What is wrong with <paramref name="relativeDirectory"/> ('/'-separated) under the project root: a part that is a symbolic
    /// link or a file, named relative to the root. Null when every part is a real directory or does not exist yet.
    /// </summary>
    public static string? Problem(string projectRoot, string relativeDirectory)
    {
        string path = projectRoot;
        string shown = string.Empty;
        foreach (string segment in relativeDirectory.Split('/'))
        {
            path = Path.Combine(path, segment);
            shown = shown.Length == 0 ? segment : $"{shown}/{segment}";
            if (new FileInfo(path).LinkTarget is not null)
            {
                return $"{shown} is a symbolic link";
            }

            if (File.Exists(path))
            {
                return $"{shown} is a file, not a directory";
            }
        }

        return null;
    }
}
