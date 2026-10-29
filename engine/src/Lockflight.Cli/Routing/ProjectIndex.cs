namespace Lockflight.Cli.Routing;

/// <summary>
/// The project files of one side of a change, found from its path listing without building: which project owns a path (the
/// nearest ancestor directory holding a <c>*.csproj</c>, <c>*.fsproj</c> or <c>*.vbproj</c>), and the <c>Directory.Build.props</c>
/// files MSBuild would import for it (every one from its directory up to the root).
/// </summary>
internal sealed class ProjectIndex
{
    private const string DirectoryBuildProps = "Directory.Build.props";
    private static readonly string[] ProjectSuffixes = [".csproj", ".fsproj", ".vbproj"];

    private readonly Dictionary<string, List<string>> projectsByDirectory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> propsByDirectory = new(StringComparer.OrdinalIgnoreCase);

    public ProjectIndex(IEnumerable<string> paths)
    {
        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            if (IsProjectFile(path))
            {
                string directory = DirectoryOf(path);
                if (!projectsByDirectory.TryGetValue(directory, out List<string>? projects))
                {
                    projects = [];
                    projectsByDirectory[directory] = projects;
                }

                projects.Add(path);
            }
            else if (FileName(path).Equals(DirectoryBuildProps, StringComparison.OrdinalIgnoreCase))
            {
                propsByDirectory.TryAdd(DirectoryOf(path), path);
            }
        }
    }

    public static bool IsProjectFile(string path) => ProjectSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>The project files in the nearest ancestor directory of <paramref name="path"/> that holds any, sorted; empty when none.</summary>
    public IReadOnlyList<string> OwningProjects(string path)
    {
        for (string? directory = DirectoryOf(path); directory is not null; directory = ParentOf(directory))
        {
            if (projectsByDirectory.TryGetValue(directory, out List<string>? projects))
            {
                return projects;
            }
        }

        return [];
    }

    /// <summary>The <c>Directory.Build.props</c> files from <paramref name="projectFile"/>'s directory up to the root, nearest first.</summary>
    public IReadOnlyList<string> PropsChain(string projectFile)
    {
        List<string> chain = [];
        for (string? directory = DirectoryOf(projectFile); directory is not null; directory = ParentOf(directory))
        {
            if (propsByDirectory.TryGetValue(directory, out string? props))
            {
                chain.Add(props);
            }
        }

        return chain;
    }

    /// <summary>The directory of a '/'-separated relative path; empty at the root.</summary>
    public static string DirectoryOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    public static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string? ParentOf(string directory) => directory.Length == 0 ? null : DirectoryOf(directory);
}
