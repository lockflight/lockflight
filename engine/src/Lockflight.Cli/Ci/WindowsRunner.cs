using Lockflight.Cli.Projects;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Whether the judge can build a tree on this runner: a test project that needs Windows (a <c>net*-windows</c> framework, WPF or
/// Windows Forms) does not build on Linux or macOS, and with <c>EnableWindowsTargeting</c> its test host still cannot start, so
/// the red commits and green would be judged on tests that never ran. Off Windows such a tree is not built at all.
/// </summary>
internal static class WindowsRunner
{
    private const int NamedProjects = 3;

    /// <summary>Why the tree cannot be judged here; null when it can (the runner is Windows, or no test project needs it).</summary>
    public static string? Why(string treeRoot, bool onWindows)
    {
        if (onWindows)
        {
            return null;
        }

        IReadOnlyList<string> projects = ProjectFiles.WindowsTestProjects(treeRoot);
        if (projects.Count == 0)
        {
            return null;
        }

        string[] names = [.. projects.Take(NamedProjects).Select(project => Path.GetRelativePath(treeRoot, project).Replace('\\', '/'))];
        string more = projects.Count > NamedProjects ? $" and {projects.Count - NamedProjects} more" : string.Empty;
        return $"{string.Join(", ", names)}{more} {(projects.Count == 1 ? "targets" : "target")} Windows, and this runner is not Windows: "
            + "nothing was built, because those tests cannot run here";
    }
}
