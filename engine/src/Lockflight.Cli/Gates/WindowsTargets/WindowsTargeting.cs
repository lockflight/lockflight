using System.Text.RegularExpressions;
using Lockflight.Cli.Gates.Affected;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Gates.WindowsTargets;

/// <summary>
/// Finds the projects of a solution that target Windows, from their project files and the <c>Directory.Build.props</c> files
/// above them, without building: a project targets Windows when one of its <c>TargetFramework</c> or <c>TargetFrameworks</c> values
/// names a <c>-windows</c> platform. Off Windows such a project builds only with <see cref="BuildProperty"/>, and its tests cannot
/// start (the host has no <c>Microsoft.WindowsDesktop.App</c>). A framework set by a property or an import other than those files
/// is not seen; such a project is treated as not targeting Windows.
/// </summary>
internal static partial class WindowsTargeting
{
    /// <summary>The MSBuild property that lets the SDK build a Windows-targeted project on another operating system.</summary>
    public const string BuildProperty = "-p:EnableWindowsTargeting=true";

    private const string PropsFileName = "Directory.Build.props";

    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

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
            string solutionDirectory = Path.GetDirectoryName(solution)!;
            string[] windows = [.. entries.Where(entry => TargetsWindows(SolutionEntries.FullPath(solution, entry), solutionDirectory))];
            if (windows.Length == 0)
            {
                return null;
            }

            HashSet<string> tests = [.. entries.Where(entry => IsTestProject(SolutionEntries.FullPath(solution, entry), solutionDirectory))];
            return new WindowsTargetedSolution(solution, entries, windows, tests);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The extra <c>dotnet build</c> arguments of a run: <see cref="BuildProperty"/> when it leaves Windows tests out
    /// (<see cref="GateRunContext.LeavesOutWindowsTests"/>) and its solution has a project that targets Windows; none otherwise.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(GateRunContext run) =>
        run.LeavesOutWindowsTests && Find(run.Target, run.BuildFile) is not null ? [BuildProperty] : [];

    /// <summary>Whether a project file, or a <c>Directory.Build.props</c> between it and the solution, names a Windows framework.</summary>
    public static bool TargetsWindows(string projectFile, string solutionDirectory) =>
        TextsOf(projectFile, solutionDirectory).Any(text => TargetFrameworks().Matches(ProjectText.WithoutComments(text))
            .Any(match => match.Groups["value"].Value.Split(';', StringSplitOptions.TrimEntries).Any(IsWindowsFramework)));

    /// <summary><c>net8.0-windows</c>, <c>net8.0-windows10.0.19041.0</c>: a framework with the Windows platform.</summary>
    public static bool IsWindowsFramework(string framework) =>
        framework.Contains("-windows", StringComparison.OrdinalIgnoreCase);

    private static bool IsTestProject(string projectFile, string solutionDirectory) =>
        TextsOf(projectFile, solutionDirectory).Any(ProjectText.MakesTestProject);

    /// <summary>The project file's text, then the text of each <c>Directory.Build.props</c> above it up to the solution's directory.</summary>
    private static IEnumerable<string> TextsOf(string projectFile, string solutionDirectory)
    {
        if (!File.Exists(projectFile))
        {
            yield break;
        }

        yield return File.ReadAllText(projectFile);
        string root = Path.GetFullPath(solutionDirectory).TrimEnd(Path.DirectorySeparatorChar);
        for (string? directory = Path.GetDirectoryName(projectFile); directory is not null; directory = Path.GetDirectoryName(directory))
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
        if (buildFile is not null)
        {
            string path = Path.GetFullPath(buildFile, target);
            return SolutionExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
        }

        string[] files = Directory.Exists(target) ? [.. Directory.EnumerateFiles(target)] : [];
        string[] solutions = [.. files.Where(file => SolutionExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))];
        bool projectBeside = files.Any(file => ProjectExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
        return solutions.Length == 1 && !projectBeside ? solutions[0] : null;
    }

    [GeneratedRegex(@"<TargetFrameworks?\b[^>]*>(?<value>[^<]*)</TargetFrameworks?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TargetFrameworks();
}
