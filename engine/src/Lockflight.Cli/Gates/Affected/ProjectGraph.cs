namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// Reads a solution's project-reference graph from the project files as text, without MSBuild (<see cref="ProjectFileReading"/>):
/// the solution's projects and every project they reference, transitively. Whatever the text cannot settle makes the graph
/// unknown rather than smaller — a reference or include through an MSBuild property other than the file's or project's own
/// directory, a wildcard or missing reference, an imported or inherited file that adds project references or lies outside the
/// repository, a project type other than C#, F# or Visual Basic, a project outside the repository or inside a nested one — so a
/// project the graph misses can never go unbuilt.
/// </summary>
internal static class ProjectGraph
{
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    /// <param name="describe">How a path is named in a problem (relative to the project root).</param>
    public static ProjectGraphRead Read(string solutionFile, IReadOnlyList<string> entries, string repositoryRoot, Func<string, string> describe)
    {
        Dictionary<string, GraphProject> projects = new(AffectedPaths.Comparer);
        Dictionary<string, string?> solutionEntries = new(AffectedPaths.Comparer);
        Queue<string> pending = new();
        foreach (string entry in entries)
        {
            string file = SolutionEntries.FullPath(solutionFile, entry);
            if (!ProjectExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                return ProjectGraphRead.Unknown($"{describe(file)} is not a C#, F# or Visual Basic project, so its references are not read");
            }

            if (solutionEntries.TryAdd(file, entry))
            {
                pending.Enqueue(file);
            }
        }

        bool inheritedTestHints = false;
        HashSet<string> msBuildFiles = new(AffectedPaths.Comparer);
        while (pending.TryDequeue(out string? file))
        {
            if (projects.ContainsKey(file))
            {
                continue;
            }

            if (Placement(file, repositoryRoot, describe) is string misplaced)
            {
                return ProjectGraphRead.Unknown(misplaced);
            }

            ProjectFileReading read = ProjectFileReading.Of(file, repositoryRoot, describe);
            if (read.Problem is string problem)
            {
                return ProjectGraphRead.Unknown(problem);
            }

            inheritedTestHints |= read.InheritedTestHints;
            msBuildFiles.UnionWith(read.MsBuildFiles);
            projects[file] = new GraphProject(file, solutionEntries.GetValueOrDefault(file), read.References, read.ExtraInputs, read.DeclaresTests);
            foreach (string reference in read.References)
            {
                pending.Enqueue(reference);
            }
        }

        // The solution's projects first, in its order, then the ones only referenced, in the order they were reached.
        return new ProjectGraphRead([.. projects.Values.OrderBy(project => project.SolutionEntry is null)], Problem: null, inheritedTestHints)
        {
            MsBuildFiles = [.. msBuildFiles.Order(StringComparer.Ordinal)],
        };
    }

    /// <summary>Why a project's changes would be invisible to the repository's git: outside it, or inside a nested repository.</summary>
    private static string? Placement(string file, string repositoryRoot, Func<string, string> describe)
    {
        if (!AffectedPaths.IsUnder(file, repositoryRoot))
        {
            return $"{describe(file)} lies outside this repository, whose git does not see its changes";
        }

        for (string? directory = Path.GetDirectoryName(file); directory is not null && AffectedPaths.IsUnder(directory, repositoryRoot) && !AffectedPaths.Same(directory, repositoryRoot); directory = Path.GetDirectoryName(directory))
        {
            if (Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git")))
            {
                return $"{describe(file)} lies in a separate repository ({describe(directory)}), whose changes this repository's git does not see";
            }
        }

        return null;
    }
}
