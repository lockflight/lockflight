using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Review;

/// <summary>
/// The test project a finding's reproduction is built in: the one that contains the cited file, else the one closest to the
/// project that contains it through project references (a direct reference beats one through another project), else the first.
/// A reproduction compiled in a project that cannot see the cited code never compiles, whatever the reviewer writes.
/// </summary>
internal static class NearestTestProject
{
    private const int MaxReferenceDepth = 8;

    public static TestApp Choose(IReadOnlyList<TestApp> apps, string root, string file)
    {
        string cited = Path.GetFullPath(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar)));
        TestApp? containing = apps
            .Where(app => IsUnder(cited, app.ProjectDirectory))
            .OrderByDescending(app => app.ProjectDirectory.Length)
            .FirstOrDefault();
        if (containing is not null)
        {
            return containing;
        }

        if (OwningProject(cited, root) is not string owner)
        {
            return apps[0];
        }

        TestApp nearest = apps[0];
        int best = int.MaxValue;
        foreach (TestApp app in apps)
        {
            if (TestProjectProfiles.ProjectFile(app.ProjectDirectory, app.AssemblyName) is string project
                && ReferenceDepth(project, owner) is int depth && depth < best)
            {
                (nearest, best) = (app, depth);
            }
        }

        return nearest;
    }

    /// <summary>The project file in the nearest directory above the file, up to the root; null when there is none.</summary>
    public static string? OwningProject(string file, string root)
    {
        string top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (DirectoryInfo? folder = new FileInfo(file).Directory; folder is not null && IsUnder(folder.FullName, top); folder = folder.Parent)
        {
            if (folder.Exists && TestProjectProfiles.ProjectFile(folder.FullName) is string project)
            {
                return project;
            }
        }

        return null;
    }

    /// <summary>How many project references lead from <paramref name="from"/> to <paramref name="target"/>; null when none do.</summary>
    private static int? ReferenceDepth(string from, string target)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(from) };
        List<string> level = [Path.GetFullPath(from)];
        for (int depth = 1; depth <= MaxReferenceDepth && level.Count > 0; depth++)
        {
            List<string> next = [];
            foreach (string reference in level.Where(File.Exists).SelectMany(TestProjectProfiles.ProjectReferences))
            {
                if (string.Equals(reference, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    return depth;
                }

                if (seen.Add(reference))
                {
                    next.Add(reference);
                }
            }

            level = next;
        }

        return null;
    }

    private static bool IsUnder(string path, string directory)
    {
        string parent = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string child = Path.GetFullPath(path);
        return string.Equals(child, parent, StringComparison.OrdinalIgnoreCase)
            || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
