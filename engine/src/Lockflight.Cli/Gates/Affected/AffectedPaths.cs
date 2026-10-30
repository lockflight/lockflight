namespace Lockflight.Cli.Gates.Affected;

/// <summary>Full-path comparisons for the project graph: case-sensitive on Linux, insensitive where the file system usually is.</summary>
internal static class AffectedPaths
{
    public static StringComparison Comparison { get; } = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static StringComparer Comparer { get; } = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static bool Same(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second), Comparison);

    /// <summary>Whether <paramref name="path"/> is <paramref name="directory"/> or lies inside it.</summary>
    public static bool IsUnder(string path, string directory)
    {
        string root = Path.TrimEndingDirectorySeparator(directory);
        return Same(path, root) || path.StartsWith(root + Path.DirectorySeparatorChar, Comparison);
    }
}
