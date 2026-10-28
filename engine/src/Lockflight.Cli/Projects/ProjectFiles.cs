using System.Text.RegularExpressions;

namespace Lockflight.Cli.Projects;

/// <summary>
/// Reads .NET project files as text, without MSBuild: which projects are test projects, which packages they reference and which
/// frameworks they target. Properties set in <c>Directory.Build.props</c>/<c>.targets</c> (and global references in
/// <c>Directory.Packages.props</c>) above a project count; values computed from MSBuild properties (<c>$(…)</c>) are not resolved.
/// </summary>
internal static partial class ProjectFiles
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", "node_modules", ".lockflight" };
    private static readonly string[] InheritedFiles = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];
    private static readonly string[] TestMarkers = ["<IsTestProject>true", "xunit", "MSTest", "NUnit", "TUnit", "Microsoft.NET.Test.Sdk"];

    /// <summary>The C# projects under the root that reference a test framework or declare themselves test projects, sorted.</summary>
    public static IReadOnlyList<string> TestProjects(string root) =>
        [.. Enumerate(root).Where(project => TestMarkers.Any(marker => File.ReadAllText(project).Contains(marker, StringComparison.OrdinalIgnoreCase))).Order(StringComparer.Ordinal)];

    /// <summary>Every C# project under the root (outside build output, <c>.git</c> and <c>.lockflight</c>), sorted.</summary>
    public static IReadOnlyList<string> All(string root) => [.. Enumerate(root).Order(StringComparer.Ordinal)];

    /// <summary>Whether the project, or a Directory.Build/Packages file between it and the root, references the package.</summary>
    public static bool References(string root, string projectPath, string package) =>
        WithInheritedFiles(root, projectPath).Any(file => PackageReferences().Matches(File.ReadAllText(file))
            .Any(match => string.Equals(match.Groups["package"].Value, package, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The target frameworks a project builds for (<c>net8.0</c>, <c>net10.0-windows</c>, …); its own setting wins over inherited ones.</summary>
    public static IReadOnlyList<string> TargetFrameworks(string root, string projectPath)
    {
        foreach (string file in WithInheritedFiles(root, projectPath))
        {
            string[] frameworks = [.. TargetFrameworkElements().Matches(File.ReadAllText(file))
                .SelectMany(match => match.Groups["value"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(framework => !framework.Contains("$(", StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
            if (frameworks.Length > 0)
            {
                return frameworks;
            }
        }

        return [];
    }

    private static IEnumerable<string> Enumerate(string root)
    {
        Stack<string> pending = new([root]);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            string[] children;
            string[] projects;
            try
            {
                children = [.. Directory.EnumerateDirectories(directory)];
                projects = [.. Directory.EnumerateFiles(directory, "*.csproj")];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string project in projects)
            {
                yield return project;
            }

            foreach (string child in children.Where(child => !SkippedDirectories.Contains(Path.GetFileName(child))))
            {
                pending.Push(child);
            }
        }
    }

    /// <summary>The project file first, then the inherited files from its directory up to the root (nearest first).</summary>
    private static IEnumerable<string> WithInheritedFiles(string root, string projectPath)
    {
        yield return projectPath;
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        for (string? directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
             directory is not null && directory.Length >= fullRoot.Length;
             directory = Path.GetDirectoryName(directory))
        {
            foreach (string name in InheritedFiles)
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    [GeneratedRegex(@"<(?:Global)?PackageReference\b[^>]*?\bInclude\s*=\s*""(?<package>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex PackageReferences();

    [GeneratedRegex(@"<TargetFrameworks?>(?<value>[^<]+)</TargetFrameworks?>", RegexOptions.IgnoreCase)]
    private static partial Regex TargetFrameworkElements();
}
