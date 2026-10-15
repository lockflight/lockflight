namespace Lockflight.Cli.Adoption;

/// <summary>
/// Directories that look like units of a workspace: a child directory — or one level further, inside a grouping folder
/// such as <c>services/</c> — with its own repository, a solution or a <c>package.json</c>. Hidden folders and <c>specs/</c> are
/// never units. When the root builds by itself (a solution or project file at the root), it is one repository, not a monorepo
/// of units: only child directories that are their own repository are units, so a <c>web/</c> folder with a
/// <c>package.json</c> never turns the root's own projects into code that no gate builds.
/// </summary>
internal static class UnitDiscovery
{
    public static IReadOnlyList<UnitCandidate> Find(string root)
    {
        IEnumerable<string> level1 = Directory.EnumerateDirectories(root).Where(directory => !Path.GetFileName(directory).StartsWith('.') && Path.GetFileName(directory) != "specs");
        IEnumerable<string> candidates = level1.Concat(level1.Where(directory => !LooksLikeUnit(directory)).SelectMany(Directory.EnumerateDirectories));
        Func<string, bool> isUnit = BuildsByItself(root) ? IsRepository : LooksLikeUnit;
        return [.. candidates.Where(isUnit).Order(StringComparer.Ordinal).Select(directory => new UnitCandidate(
            Path.GetRelativePath(root, directory).Replace('\\', '/'),
            IsRepository(directory),
            HasSolution(directory)))];
    }

    public static bool IsRepository(string directory) => Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git"));

    private static bool BuildsByItself(string root) =>
        HasSolution(root) || Directory.EnumerateFiles(root, "*.csproj").Any();

    private static bool HasSolution(string directory) =>
        Directory.EnumerateFiles(directory).Any(file => file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeUnit(string directory) =>
        IsRepository(directory) || HasSolution(directory) || File.Exists(Path.Combine(directory, "package.json"));
}
