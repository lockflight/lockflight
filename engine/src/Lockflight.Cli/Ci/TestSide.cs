using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The test side of a tree, '/'-separated relative paths: each test project's directory by assembly, the shared test-helper
/// projects (projects that reference xUnit and are not test applications), and the inputs outside the projects (a root
/// <c>xunit.runner.json</c>, root <c>*.runsettings</c>, <c>ci.testInputs</c>). A test's C# code is compared by its fingerprint; the
/// rest of the test side by content, because a test cannot be tied to the files it reads.
/// </summary>
internal sealed record TestSide(IReadOnlyDictionary<string, string> ProjectsByAssembly, IReadOnlyList<string> HelperProjects, IReadOnlyList<string> InputGlobs)
{
    private static readonly string[] RootInputs = ["xunit.runner.json", "*.runsettings"];

    /// <summary>Attributes that declare a test in xUnit, NUnit or MSTest; a file with none of them is a helper.</summary>
    private static readonly string[] TestMarkers = ["[Fact", "[Theory", "[Test", "[TestCase", "[TestMethod", "[DataTestMethod"];

    /// <param name="treeRoot">Where the head's tree was built: the test applications' directories are under it.</param>
    public static async Task<TestSide> ReadAsync(string repositoryRoot, string head, TreeListing headListing, string treeRoot, CiSettings ci, CancellationToken cancellationToken)
    {
        Dictionary<string, string> projects = new(StringComparer.Ordinal);
        foreach (TestApp app in headListing.Workspace.Apps)
        {
            string relative = Path.GetRelativePath(treeRoot, app.ProjectDirectory).Replace('\\', '/');
            projects[app.AssemblyName] = relative == "." ? string.Empty : relative;
        }

        List<string> helpers = [];
        foreach (string project in (await JudgeGit.TreeFilesAsync(repositoryRoot, head, cancellationToken) ?? []).Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            string directory = Path.GetDirectoryName(project)?.Replace('\\', '/') ?? string.Empty;
            if (!projects.ContainsValue(directory)
                && await JudgeGit.ShowFileAsync(repositoryRoot, head, project, cancellationToken) is string text && text.Contains("xunit", StringComparison.OrdinalIgnoreCase))
            {
                helpers.Add(directory);
            }
        }

        return new TestSide(projects, helpers, [.. RootInputs, .. ci.TestInputs]);
    }

    /// <summary>The files of a tree listing that are on the test side.</summary>
    public IReadOnlyList<string> Of(IReadOnlyList<string> files) => [.. files.Where(IsTestSide).Distinct(StringComparer.Ordinal)];

    public bool IsTestSide(string path) =>
        ProjectsByAssembly.Values.Any(directory => Under(path, directory)) || HelperProjects.Any(directory => Under(path, directory)) || IsInput(path);

    /// <summary>Whether a change to <paramref name="path"/> can change what a test of <paramref name="assembly"/> does beyond its fingerprint.</summary>
    public bool AffectsBeyondCode(string path, string assembly) =>
        IsInput(path)
        || HelperProjects.Any(directory => Under(path, directory))
        || (ProjectsByAssembly.TryGetValue(assembly, out string? project) && Under(path, project) && !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !IsBuildOutput(path, project));

    /// <summary>
    /// The C# files among <paramref name="paths"/> that are in a test project and declare no test at <paramref name="commit"/>:
    /// helpers and fixtures, compared by content because a test can depend on them in ways its fingerprint walk cannot follow.
    /// </summary>
    public async Task<IReadOnlySet<string>> HelperSourcesAsync(string root, string commit, IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        HashSet<string> helpers = new(StringComparer.Ordinal);
        foreach (string path in paths.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && ProjectsByAssembly.Values.Any(directory => Under(path, directory))))
        {
            if (await JudgeGit.ShowFileAsync(root, commit, path, cancellationToken) is string text && !TestMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
            {
                helpers.Add(path);
            }
        }

        return helpers;
    }

    /// <summary>Whether a changed file can change what a test of <paramref name="assembly"/> does beyond its fingerprint.</summary>
    public bool Affects(string path, string assembly, IReadOnlySet<string> helperSources) =>
        AffectsBeyondCode(path, assembly)
        || (helperSources.Contains(path) && ProjectsByAssembly.TryGetValue(assembly, out string? project) && Under(path, project));

    private bool IsInput(string path) => InputGlobs.Any(glob => PathRules.Matches(glob, path));

    private static bool Under(string path, string directory) =>
        directory.Length == 0 || directory == "." || path.StartsWith(directory + "/", StringComparison.Ordinal);

    private static bool IsBuildOutput(string path, string project)
    {
        string rest = project.Length == 0 || project == "." ? path : path[(project.Length + 1)..];
        return rest.StartsWith("bin/", StringComparison.Ordinal) || rest.StartsWith("obj/", StringComparison.Ordinal) || rest.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    }
}
