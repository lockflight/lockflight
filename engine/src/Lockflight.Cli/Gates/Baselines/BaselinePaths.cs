namespace Lockflight.Cli.Gates.Baselines;

/// <summary>
/// Where a baseline file lives: <c>.lockflight/baselines/</c> for a single repository, <c>.lockflight/baselines/&lt;unit&gt;/</c> for a
/// unit of a workspace, so accepting one service's failing tests or advisories never covers another's.
/// </summary>
internal static class BaselinePaths
{
    public const string Directory = ".lockflight/baselines";

    /// <summary>The baseline file's path relative to the project root, '/'-separated.</summary>
    public static string Relative(string? unitPath, string fileName) =>
        unitPath is null ? $"{Directory}/{fileName}" : $"{Directory}/{unitPath}/{fileName}";

    public static string Full(string projectRoot, string? unitPath, string fileName) =>
        Path.Combine(projectRoot, Relative(unitPath, fileName).Replace('/', Path.DirectorySeparatorChar));
}
