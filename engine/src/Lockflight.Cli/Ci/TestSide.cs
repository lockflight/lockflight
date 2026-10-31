using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The test side of a tree, '/'-separated relative paths: each test project's directory by assembly, the shared test-helper
/// projects (<see cref="TestHelperProjects"/>, the definition the red classifier uses too), and the inputs outside the projects (a
/// root <c>xunit.runner.json</c>, root <c>*.runsettings</c>, <c>ci.testInputs</c>). A test's C# code is compared by its
/// fingerprint; the rest of the test side by content, because a test cannot be tied to the files it reads: every file of a helper
/// project or an input, every non-C# file of a test project (its project file included), and every C# file of a test project that
/// declares no test or holds an assembly-wide hook (a set-up fixture, an assembly fixture, a module initializer, an
/// <c>[assembly: …]</c> attribute), whether it existed at the older commit or was added since.
/// </summary>
internal sealed partial record TestSide(IReadOnlyDictionary<string, string> ProjectsByAssembly, IReadOnlyList<string> HelperProjects, IReadOnlyList<string> InputGlobs)
{
    private static readonly string[] RootInputs = ["xunit.runner.json", "*.runsettings"];

    /// <summary>Attributes that declare a test in xUnit, NUnit or MSTest; a file with none of them is a helper.</summary>
    private static readonly string[] TestMarkers = ["[Fact", "[Theory", "[Test", "[TestCase", "[TestMethod", "[DataTestMethod"];

    /// <summary>
    /// Code that changes what other tests of the assembly do, beyond any one test's fingerprint: NUnit set-up fixtures and test
    /// actions, MSTest assembly and global initializers, xUnit collection and assembly fixtures, custom frameworks and before/after
    /// attributes, module initializers and assembly attributes.
    /// </summary>
    private static readonly string[] HookMarkers =
    [
        "SetUpFixture", "ITestAction", "IWrapTestMethod", "IWrapSetUpTearDown", "AssemblyInitialize", "AssemblyCleanup", "GlobalTestInitialize",
        "GlobalTestCleanup", "CollectionDefinition", "ICollectionFixture", "AssemblyFixture", "TestFramework", "BeforeAfterTestAttribute",
        "ModuleInitializer", "global using",
    ];
    /// <param name="treeRoot">Where the head's tree was built: the test applications' directories are under it.</param>
    public static async Task<TestSide> ReadAsync(string repositoryRoot, string head, TreeListing headListing, string treeRoot, CiSettings ci, CancellationToken cancellationToken)
    {
        Dictionary<string, string> projects = new(StringComparer.Ordinal);
        foreach (TestApp app in headListing.Workspace.Apps)
        {
            string relative = Path.GetRelativePath(treeRoot, app.ProjectDirectory).Replace('\\', '/');
            projects[app.AssemblyName] = relative == "." ? string.Empty : relative;
        }

        Dictionary<string, string> projectTexts = new(StringComparer.OrdinalIgnoreCase);
        foreach (string project in (await JudgeGit.TreeFilesAsync(repositoryRoot, head, cancellationToken) ?? []).Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            if (await JudgeGit.ShowFileAsync(repositoryRoot, head, project, cancellationToken) is string text)
            {
                projectTexts[project] = text;
            }
        }

        HashSet<string> appProjects = new(projectTexts.Keys.Where(path => projects.ContainsValue(DirectoryOf(path))), StringComparer.OrdinalIgnoreCase);
        
        // A helper project at the repository root would make every file test side; its files are not tracked as a directory.
        List<string> helpers = [.. TestHelperProjects.Find(projectTexts, appProjects).Select(DirectoryOf).Where(directory => directory.Length > 0).Distinct(StringComparer.Ordinal)];
        return new TestSide(projects, helpers, [.. RootInputs, .. ci.TestInputs]);
    }

    /// <summary>Whether <paramref name="path"/> is a test application's own project file (not a helper project's).</summary>
    public bool IsTestProjectFile(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && ProjectsByAssembly.Values.Any(directory => DirectoryOf(path) == directory);

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
    /// The C# files among <paramref name="paths"/> that are in a test project and are compared by content: a file that declares no
    /// test at <paramref name="older"/> (a helper or fixture, which a test can depend on in ways its fingerprint walk cannot
    /// follow), and a file that holds an assembly-wide hook at either commit (<paramref name="head"/> included). A file added since
    /// that holds no hook is a later cycle's tests or helpers, not an input.
    /// </summary>
    public async Task<IReadOnlySet<string>> HelperSourcesAsync(string root, string older, string head, IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        HashSet<string> helpers = new(StringComparer.Ordinal);
        foreach (string path in paths.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && ProjectsByAssembly.Values.Any(directory => Under(path, directory))))
        {
            string? before = await JudgeGit.ShowFileAsync(root, older, path, cancellationToken);
            string? after = await JudgeGit.ShowFileAsync(root, head, path, cancellationToken);
            // A file added since that declares no test can reach an older test only through a call, which the test's fingerprint
            // walk follows; it counts only when it holds a hook.
            if ((before is string text && !DeclaresTests(text)) || HasHook(before) || HasHook(after))
            {
                helpers.Add(path);
            }
        }

        return helpers;
    }

    /// <summary>
    /// The test-side files of <paramref name="headFiles"/> that a re-check on <paramref name="olderFiles"/>' code lays over it: all of
    /// them except C# files added since that declare tests and hold no hook (a later cycle's or the pull request's other tests,
    /// which may need code the older commit does not have; the tests being re-checked are not in them).
    /// </summary>
    public async Task<IReadOnlyList<string>> OverlayAsync(string root, string head, IReadOnlyList<string> headFiles, IReadOnlySet<string> olderFiles, CancellationToken cancellationToken)
    {
        List<string> overlay = [];
        foreach (string path in Of(headFiles))
        {
            if (olderFiles.Contains(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !ProjectsByAssembly.Values.Any(directory => Under(path, directory))
                || await JudgeGit.ShowFileAsync(root, head, path, cancellationToken) is not string text || !DeclaresTests(text) || HasHook(text))
            {
                overlay.Add(path);
            }
        }

        return overlay;
    }

    /// <summary>Whether a changed file can change what a test of <paramref name="assembly"/> does beyond its fingerprint.</summary>
    public bool Affects(string path, string assembly, IReadOnlySet<string> helperSources) =>
        AffectsBeyondCode(path, assembly)
        || (helperSources.Contains(path) && ProjectsByAssembly.TryGetValue(assembly, out string? project) && Under(path, project));

    private static bool DeclaresTests(string text) => TestMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));

    private static bool HasHook(string? text) =>
        text is not null && (HookMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) || AssemblyAttribute().IsMatch(text));

    /// <summary>An assembly-level attribute list: <c>[assembly: …]</c>, with any spacing.</summary>
    [GeneratedRegex(@"\[\s*assembly\s*:", RegexOptions.CultureInvariant)]
    private static partial Regex AssemblyAttribute();

    private static string DirectoryOf(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;

    private bool IsInput(string path) => InputGlobs.Any(glob => PathRules.Matches(glob, path));

    private static bool Under(string path, string directory) =>
        directory.Length == 0 || directory == "." || path.StartsWith(directory + "/", StringComparison.Ordinal);

    private static bool IsBuildOutput(string path, string project)
    {
        string rest = project.Length == 0 || project == "." ? path : path[(project.Length + 1)..];
        return rest.StartsWith("bin/", StringComparison.Ordinal) || rest.StartsWith("obj/", StringComparison.Ordinal);
    }
}
