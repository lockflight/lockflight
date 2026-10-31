using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Testing;

/// <summary>
/// The one definition of a shared test-helper project, used by the red classifier (which frames are production), tests since red
/// (which files are the test side) and the host-tampering scan (which assemblies are test code). A project that is not a test
/// application is a helper when it references a test framework (<c>xunit…</c>, <c>NUnit</c>, <c>MSTest…</c>), or when a test
/// application reaches it through project references, no production project references it, and it looks like test code: it
/// references an assertion or mocking library, or a segment of its path is <c>test</c>, <c>tests</c> or <c>testing</c>, or a word that starts with <c>Test</c>
/// (<c>tests/Shop.Support</c>, <c>Shop.Testing</c>, <c>Shop.TestSupport</c>). A library that only the tests reference but that is neither is production: a class library's own
/// project is referenced by its tests alone.
/// </summary>
internal static class TestHelperProjects
{
    /// <summary>What a test project's file names (its package references): <c>xunit…</c>, <c>NUnit</c>, <c>MSTest…</c>.</summary>
    public static readonly string[] FrameworkMarkers = ["xunit", "nunit", "mstest"];

    /// <summary>Assertion and mocking libraries that production code does not reference.</summary>
    private static readonly string[] TestLibraryMarkers = ["fluentassertions", "awesomeassertions", "shouldly", "nsubstitute", "\"moq\"", "fakeiteasy", "autofixture"];

    /// <param name="projects">Every project file, by its '/'-separated path, with its text.</param>
    /// <param name="appProjects">The paths of the test applications' project files.</param>
    /// <returns>The helper projects' paths (never a test application).</returns>
    public static IReadOnlySet<string> Find(IReadOnlyDictionary<string, string> projects, IReadOnlySet<string> appProjects)
    {
        HashSet<string> helpers = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string path, string text) in projects)
        {
            if (!appProjects.Contains(path) && FrameworkMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                helpers.Add(path);
            }
        }

        Dictionary<string, IReadOnlyList<string>> references = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string path, string text) in projects)
        {
            references[path] = [.. ProjectReferences.Of(text, path).Select(reference => reference.Target).OfType<string>().Where(projects.ContainsKey)];
        }

        HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> pending = new(appProjects.Where(projects.ContainsKey));
        HashSet<string> seen = new(pending, StringComparer.OrdinalIgnoreCase);
        while (pending.TryDequeue(out string? project))
        {
            foreach (string target in references[project].Where(seen.Add))
            {
                pending.Enqueue(target);
                if (!appProjects.Contains(target) && !helpers.Contains(target) && LooksLikeTestCode(target, projects[target]))
                {
                    candidates.Add(target);
                }
            }
        }

        bool changed = true;
        while (changed)
        {
            HashSet<string> production = [.. projects.Keys.Where(path => !appProjects.Contains(path) && !helpers.Contains(path) && !candidates.Contains(path))];
            changed = candidates.RemoveWhere(candidate => production.Any(project => references[project].Contains(candidate, StringComparer.OrdinalIgnoreCase))) > 0;
        }

        helpers.UnionWith(candidates);
        return helpers;
    }

    private static bool LooksLikeTestCode(string path, string text) =>
        TestLibraryMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase))
        || path.Split(['/', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries).SkipLast(1).Any(IsTestSegment);

    /// <summary><c>test</c>, <c>tests</c>, <c>testing</c>, or a word that starts with <c>Test</c> (<c>TestSupport</c>), not <c>Testimonials</c>.</summary>
    private static bool IsTestSegment(string segment) =>
        segment.Equals("test", StringComparison.OrdinalIgnoreCase) || segment.Equals("tests", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("testing", StringComparison.OrdinalIgnoreCase)
        || (segment.Length > 4 && segment.StartsWith("Test", StringComparison.Ordinal) && char.IsUpper(segment[4]));
}
