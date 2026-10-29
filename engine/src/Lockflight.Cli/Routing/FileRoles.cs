using Lockflight.Cli.Paths;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Gives a path its <see cref="FileRole"/> on one side of a change, first match wins, every name test ignoring case. A file's
/// project is the nearest one on that side; the caller reads the role at base for a file that exists there, so editing a project
/// file in the pull request cannot turn its existing files into tests. When unsure, the role is the one that counts: a
/// <c>.cs</c> file under no project is production, a project that is not clearly a test project is production, a file of a test
/// project nested inside a production project's directory is production (that project compiles it too), and a file the
/// generated list names only counts as generated when its text has the generated shape.
/// </summary>
internal sealed class FileRoles(RevisionSnapshot side, IReadOnlyList<string> excludedGlobs)
{
    private const string FeatureRecords = ".lockflight/features/";
    private const string DocsDirectory = "docs/";

    private static readonly string[] SolutionSuffixes = [".sln", ".slnx", ".slnf"];
    private static readonly string[] BuildWideNames =
    [
        "Directory.Build.props", "Directory.Build.targets", "Directory.Build.rsp", "Directory.Packages.props", "global.json",
        "nuget.config", ".editorconfig", ".gitattributes",
    ];

    private static readonly string[] BuildWideSuffixes = [".props", ".targets", ".globalconfig"];
    private static readonly string[] TestConfigNames = ["xunit.runner.json", "testconfig.json"];
    private static readonly string[] TestConfigSuffixes = [".runsettings"];
    private static readonly string[] CSharpSuffixes = [".cs"];
    private static readonly string[] MarkupSuffixes = [".razor", ".cshtml"];
    private static readonly string[] OtherSourceSuffixes = [".fs", ".fsi", ".vb"];
    private static readonly string[] DocsSuffixes = [".md"];
    private static readonly string[] LooseDocsSuffixes = [".txt", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".webp", ".bmp"];

    private readonly ProjectIndex projects = new(side.Paths);
    private readonly Dictionary<string, bool> testProjects = new(StringComparer.Ordinal);

    public ProjectIndex Projects => projects;

    public FileRoleAt Of(string path)
    {
        IReadOnlyList<string> owners = projects.OwningProjects(path);
        string? project = owners.Count == 0 ? null : owners[0];
        // A production project's default globs compile every file below it, a nested test project's files included.
        bool inTestProject = owners.Count > 0 && owners.All(IsTestProject)
            && projects.AncestorProjects(path).Where(ancestor => !ProjectIndex.IsProjectFile(path) || ancestor != path).All(IsTestProject);
        if (!inTestProject && owners.Count > 1)
        {
            project = owners.First(owner => !IsTestProject(owner));
        }

        return new FileRoleAt(RoleOf(path, project, inTestProject), project);
    }

    /// <summary>Whether a project file (with the <c>Directory.Build.props</c> files above it) makes a test project on this side.</summary>
    public bool IsTestProject(string projectFile)
    {
        if (!testProjects.TryGetValue(projectFile, out bool isTest))
        {
            isTest = side.TextOf(projectFile) is string text
                && (ProjectText.MakesTestProject(text) || projects.PropsChain(projectFile).Any(props => side.TextOf(props) is string propsText && ProjectText.MakesTestProject(propsText)));
            testProjects[projectFile] = isTest;
        }

        return isTest;
    }

    private FileRole RoleOf(string path, string? project, bool inTestProject)
    {
        string name = ProjectIndex.FileName(path);
        if (excludedGlobs.Any(glob => PathRules.Matches(glob, path)))
        {
            return FileRole.NonProduction;
        }

        if (GeneratedFiles.IsGenerated(path, side.TextOf(path), side.Contains))
        {
            return FileRole.Generated;
        }

        if (ProjectIndex.IsProjectFile(path) || EndsWithAny(name, SolutionSuffixes))
        {
            return FileRole.Project;
        }

        if (BuildWideNames.Contains(name, StringComparer.OrdinalIgnoreCase) || EndsWithAny(name, BuildWideSuffixes))
        {
            return FileRole.BuildWide;
        }

        if (TestConfigNames.Contains(name, StringComparer.OrdinalIgnoreCase) || EndsWithAny(name, TestConfigSuffixes))
        {
            return FileRole.TestConfig;
        }

        if (inTestProject)
        {
            return FileRole.Test;
        }

        if (EndsWithAny(name, CSharpSuffixes) || EndsWithAny(name, OtherSourceSuffixes))
        {
            return FileRole.Production;
        }

        if (EndsWithAny(name, MarkupSuffixes))
        {
            return FileRole.Markup;
        }

        if (path.StartsWith(FeatureRecords, StringComparison.OrdinalIgnoreCase) || EndsWithAny(name, DocsSuffixes))
        {
            return FileRole.Docs;
        }

        if (project is null && (EndsWithAny(name, LooseDocsSuffixes) || path.StartsWith(DocsDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            return FileRole.Docs;
        }

        return project is null ? FileRole.Other : FileRole.ProductionConfig;
    }

    private static bool EndsWithAny(string name, string[] suffixes) => suffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}
