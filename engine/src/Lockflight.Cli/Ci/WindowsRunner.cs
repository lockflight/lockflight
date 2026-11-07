using Lockflight.Cli.Gates.WindowsTargets;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Whether the judge can build a tree on this runner: a test project it builds that needs Windows (<see cref="WindowsTargeting"/>)
/// cannot start its tests on Linux or macOS, even when <c>EnableWindowsTargeting</c> lets it build, so the red commits and green
/// would be judged on tests that never ran. Off Windows such a tree is not built at all. A project that targets Windows and is not
/// a test project (a WPF app with plain tests) is no reason: it builds with <c>EnableWindowsTargeting</c>.
/// </summary>
internal static class WindowsRunner
{
    private const int NamedProjects = 3;

    /// <summary>
    /// Why the tree cannot be judged here; null when it can (the runner is Windows, or no test project the
    /// <paramref name="scopes"/> build needs it).
    /// </summary>
    public static string? Why(string treeRoot, IReadOnlyList<BuildScope> scopes, bool onWindows)
    {
        if (onWindows)
        {
            return null;
        }

        IReadOnlyList<string> projects = WindowsTargeting.WindowsTestProjects(scopes);
        if (projects.Count == 0)
        {
            return null;
        }

        return $"{Describe(treeRoot, projects)}, and this runner is not Windows: nothing was built, because those tests cannot run here";
    }

    /// <summary>The projects relative to the root (at most three named) with "targets Windows" or "target Windows".</summary>
    public static string Describe(string root, IReadOnlyList<string> projects)
    {
        string[] names = [.. projects.Take(NamedProjects).Select(project => Path.GetRelativePath(root, project).Replace('\\', '/'))];
        string more = projects.Count > NamedProjects ? $" and {projects.Count - NamedProjects} more" : string.Empty;
        return $"{string.Join(", ", names)}{more} {(projects.Count == 1 ? "targets" : "target")} Windows";
    }
}
