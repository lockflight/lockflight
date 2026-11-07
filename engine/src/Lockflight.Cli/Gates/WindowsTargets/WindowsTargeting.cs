using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lockflight.Cli.Gates.Affected;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Gates.WindowsTargets;

/// <summary>
/// The one place that decides which projects target Windows, read from the project files and the <c>Directory.Build.props</c>
/// files above them without building, and only among the projects a build actually builds (<see cref="BuildScope"/>): a project
/// elsewhere in the tree (a sample, a tool) is not built and does not count. A project targets Windows when its target framework
/// names a <c>-windows</c> platform (<c>TargetFramework</c> wins over <c>TargetFrameworks</c>, the project's own setting over an
/// inherited one; a conditional value counts), or when <c>UseWPF</c> or <c>UseWindowsForms</c> is true. XML comments are ignored.
/// Off Windows such a project builds only with <see cref="BuildProperty"/>, and its tests cannot start (the host has no
/// <c>Microsoft.WindowsDesktop.App</c>). A framework set by a computed property or another import is not seen.
/// </summary>
internal static partial class WindowsTargeting
{
    /// <summary>The MSBuild property that lets the SDK build a Windows-targeted project on another operating system.</summary>
    public const string BuildProperty = "-p:EnableWindowsTargeting=true";

    private const string PropsFileName = "Directory.Build.props";
    private const string WindowsPlatform = "-windows";

    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];
    private static readonly Regex[] DesktopProperties = [UseWpf(), UseWindowsForms()];

    /// <summary>
    /// The solution the gates run in the target (the build file when it is a solution, else the target's only solution) when it
    /// lists a project that targets Windows; null otherwise, or when the files cannot be read.
    /// </summary>
    public static WindowsTargetedSolution? Find(string target, string? buildFile)
    {
        if (SolutionOf(target, buildFile) is not string solution || SolutionEntries.Read(solution) is not IReadOnlyList<string> entries)
        {
            return null;
        }

        try
        {
            string[] windows = [.. entries.Where(entry => TargetsWindows(SolutionEntries.FullPath(solution, entry), target))];
            if (windows.Length == 0)
            {
                return null;
            }

            HashSet<string> tests = [.. entries.Where(entry => IsTestProject(SolutionEntries.FullPath(solution, entry), target))];
            return new WindowsTargetedSolution(solution, entries, windows, tests);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The extra <c>dotnet build</c> arguments of a gate run: <see cref="BuildProperty"/> when it runs off Windows
    /// (<see cref="GateRunContext.OffWindows"/>) and builds a project that targets Windows; none otherwise.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(GateRunContext run) =>
        run.OffWindows && BuildsWindowsProjects(new BuildScope(run.Target, run.BuildFile)) ? [BuildProperty] : [];

    /// <summary>The extra <c>dotnet build</c> arguments of a build off Windows of <paramref name="scope"/>; none on Windows.</summary>
    public static IReadOnlyList<string> BuildArguments(BuildScope scope, bool onWindows) =>
        !onWindows && BuildsWindowsProjects(scope) ? [BuildProperty] : [];

    /// <summary>Whether a project the scope builds targets Windows.</summary>
    public static bool BuildsWindowsProjects(BuildScope scope) =>
        ProjectsBuilt(scope).Any(project => TargetsWindows(project, scope.Directory));

    /// <summary>The test projects the scopes build that target Windows, as full paths, sorted and without duplicates.</summary>
    public static IReadOnlyList<string> WindowsTestProjects(IEnumerable<BuildScope> scopes) =>
        [.. scopes.SelectMany(scope => ProjectsBuilt(scope).Where(project => IsTestProject(project, scope.Directory) && TargetsWindows(project, scope.Directory)))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>
    /// The projects a build of the scope builds, as full paths: the entries of its solution or solution filter, or its project.
    /// Empty when that is unknown (no build file and not exactly one solution or project in the directory) or unreadable.
    /// </summary>
    public static IReadOnlyList<string> ProjectsBuilt(BuildScope scope)
    {
        string? file = scope.BuildFile is null ? OnlyBuildFileIn(scope.Directory) : Path.GetFullPath(scope.BuildFile, scope.Directory);
        if (file is null || !File.Exists(file))
        {
            return [];
        }

        string extension = Path.GetExtension(file);
        if (ProjectExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return [file];
        }

        if (extension.Equals(".slnf", StringComparison.OrdinalIgnoreCase))
        {
            return FilteredProjects(file);
        }

        return SolutionEntries.Read(file) is IReadOnlyList<string> entries ? [.. entries.Select(entry => SolutionEntries.FullPath(file, entry))] : [];
    }

    /// <summary>
    /// Whether a project targets Windows, reading the project file and each <c>Directory.Build.props</c> above it up to
    /// <paramref name="stopDirectory"/> (or the file system's root when the project lies outside it), nearest first.
    /// </summary>
    public static bool TargetsWindows(string projectFile, string stopDirectory)
    {
        try
        {
            string[] texts = [.. TextsOf(projectFile, stopDirectory).Select(ProjectText.WithoutComments)];
            string[] frameworks = NearestValues(texts, SingleFramework()) is { Length: > 0 } single ? single : NearestValues(texts, ManyFrameworks());
            return frameworks.SelectMany(value => value.Split(';', StringSplitOptions.TrimEntries)).Any(IsWindowsFramework)
                || DesktopProperties.Any(property => NearestValues(texts, property).Any(value => value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary><c>net8.0-windows</c>, <c>net8.0-windows10.0.19041.0</c>: a framework with the Windows platform.</summary>
    public static bool IsWindowsFramework(string framework) =>
        framework.Contains(WindowsPlatform, StringComparison.OrdinalIgnoreCase);

    private static bool IsTestProject(string projectFile, string stopDirectory) =>
        TextsOf(projectFile, stopDirectory).Any(ProjectText.MakesTestProject);

    /// <summary>The values of the first text (nearest file) that sets the property at all; empty when none does.</summary>
    private static string[] NearestValues(IEnumerable<string> texts, Regex property)
    {
        foreach (string text in texts)
        {
            string[] values = [.. property.Matches(text).Select(match => match.Groups["value"].Value)];
            if (values.Length > 0)
            {
                return values;
            }
        }

        return [];
    }

    /// <summary>The project file's text, then the text of each <c>Directory.Build.props</c> above it up to the stop directory.</summary>
    private static IEnumerable<string> TextsOf(string projectFile, string stopDirectory)
    {
        if (!File.Exists(projectFile))
        {
            yield break;
        }

        yield return File.ReadAllText(projectFile);
        string root = Path.GetFullPath(stopDirectory).TrimEnd(Path.DirectorySeparatorChar);
        for (string? directory = Path.GetDirectoryName(Path.GetFullPath(projectFile)); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            string props = Path.Combine(directory, PropsFileName);
            if (File.Exists(props))
            {
                yield return File.ReadAllText(props);
            }

            if (string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }
        }
    }

    private static string? SolutionOf(string target, string? buildFile)
    {
        string? file = buildFile is null ? OnlyBuildFileIn(target) : Path.GetFullPath(buildFile, target);
        return file is not null && SolutionExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) && File.Exists(file) ? file : null;
    }

    /// <summary>What <c>dotnet build</c> with no file builds in the directory: its only solution or project; null when there is not exactly one.</summary>
    private static string? OnlyBuildFileIn(string directory)
    {
        string[] files = Directory.Exists(directory) ? [.. Directory.EnumerateFiles(directory)] : [];
        string[] candidates = [.. files.Where(file => SolutionExtensions.Concat(ProjectExtensions).Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))];
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static IReadOnlyList<string> FilteredProjects(string filterFile)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(filterFile))?["solution"] is not JsonObject solution
                || solution["path"]?.GetValue<string>() is not string solutionPath || solution["projects"] is not JsonArray projects)
            {
                return [];
            }

            string solutionFile = Path.GetFullPath(solutionPath, Path.GetDirectoryName(filterFile)!);
            return [.. projects.Select(project => project?.GetValue<string>()).OfType<string>().Select(entry => SolutionEntries.FullPath(solutionFile, entry))];
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"<TargetFramework\b[^>]*>(?<value>[^<]*)</TargetFramework>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SingleFramework();

    [GeneratedRegex(@"<TargetFrameworks\b[^>]*>(?<value>[^<]*)</TargetFrameworks>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ManyFrameworks();

    [GeneratedRegex(@"<UseWPF\b[^>]*>(?<value>[^<]*)</UseWPF>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UseWpf();

    [GeneratedRegex(@"<UseWindowsForms\b[^>]*>(?<value>[^<]*)</UseWindowsForms>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UseWindowsForms();
}
