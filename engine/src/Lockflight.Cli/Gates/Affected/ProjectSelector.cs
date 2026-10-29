using Lockflight.Cli.Lifecycle;

namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// Chooses what the stop profile builds and tests in one solution: the projects the uncommitted changes touch and, transitively,
/// every project that references one of them (<see cref="ProjectGraph"/>). Each changed file is, in this order: skipped when it lies
/// in another unit of the workspace or in Lockflight's own directories (<c>.lockflight/</c>, <c>.claude/</c>, <c>.specify/</c>,
/// <c>specs/</c>); a reason to run everything when it is a shared build input (<c>Directory.Build.*</c>, <c>Directory.Packages.props</c>,
/// <c>global.json</c>, <c>NuGet.config</c>, any <c>.props</c>/<c>.targets</c>, a solution, <c>.editorconfig</c>); a change to the
/// project whose directory holds it, or that includes it from outside; else a reason to run everything when it lies in the target
/// or is a build input elsewhere. Everything runs, too, when anything is uncertain — no single solution, a graph the project files
/// cannot settle, git failing, no uncommitted change, nothing owned — so the choice fails toward completeness. The pr profile and
/// <c>gate run --full</c> always run everything.
/// </summary>
internal static class ProjectSelector
{
    /// <summary>The version of these rules, in the stop steps' cache key: a PASS chosen by older rules is not reused.</summary>
    public const int RulesVersion = 1;

    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];
    private static readonly string[] OwnDirectories = [".lockflight", ".claude", ".specify", FeatureLocator.SpecsDirectory];

    private static readonly HashSet<string> SharedInputNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Directory.Build.props", "Directory.Build.targets", "Directory.Build.rsp", "Directory.Packages.props", "Directory.Solution.props",
        "Directory.Solution.targets", "global.json", "nuget.config", ".editorconfig",
    };

    private static readonly HashSet<string> SharedInputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".props", ".targets", ".sln", ".slnx", ".slnf", ".rsp", ".editorconfig", ".globalconfig",
    };

    /// <summary>Source and project files: outside the target they are build inputs a unit may share (as <see cref="SharedChanges"/> counts them).</summary>
    private static readonly HashSet<string> BuildInputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".fs", ".vb", ".csproj", ".fsproj", ".vbproj", ".projitems", ".resx",
    };

    public static async Task<ProjectSelection> SelectAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        if (run.Profile != GateProfile.Stop)
        {
            return ProjectSelection.Everything("the pr profile runs every project");
        }

        if (run.FullRun)
        {
            return ProjectSelection.Everything("--full");
        }

        (string? solution, string noSolution) = SolutionOf(run);
        if (solution is null)
        {
            return ProjectSelection.Everything(noSolution);
        }

        string Describe(string path) => Path.GetRelativePath(run.ProjectRoot, path).Replace('\\', '/');
        if (SolutionEntries.Read(solution) is not IReadOnlyList<string> entries)
        {
            return ProjectSelection.Everything($"{Describe(solution)} could not be read");
        }

        if (await WorkingTreeChanges.ReadAsync(run.Target, cancellationToken) is not WorkingTreeChanges changes)
        {
            return ProjectSelection.Everything("git could not list the uncommitted changes");
        }

        if (changes.Files.Count == 0)
        {
            return ProjectSelection.Everything("no uncommitted changes to choose projects by");
        }

        ProjectGraphRead graph = ProjectGraph.Read(solution, entries, changes.RepositoryRoot, Describe);
        if (graph.Problem is string problem)
        {
            return ProjectSelection.Everything(problem);
        }

        Dictionary<string, string> changedBy = new(AffectedPaths.Comparer);
        foreach (string file in changes.Files)
        {
            if (Classify(run, graph.Projects, file, Describe) is string everything)
            {
                return ProjectSelection.Everything(everything);
            }

            foreach (GraphProject owner in Owners(graph.Projects, file))
            {
                changedBy.TryAdd(owner.File, Describe(file));
            }
        }

        if (changedBy.Count == 0)
        {
            return ProjectSelection.Everything("no project holds the uncommitted changes");
        }

        IReadOnlyList<AffectedProject> affected = Affected(graph.Projects, changedBy);
        AffectedProject[] inSolution = [.. affected.Where(project => project.SolutionEntry is not null)];
        if (inSolution.Length == entries.Count)
        {
            return ProjectSelection.Everything($"the changes affect all {entries.Count} projects");
        }

        bool runsTests = inSolution.Any(project => project.DeclaresTests);
        if (!runsTests && graph.InheritedTestHints)
        {
            return ProjectSelection.Everything("no affected project declares tests, but a Directory.Build file names a test framework, so test projects cannot be told apart");
        }

        string filter = SolutionFilter.PathFor(run.ProjectRoot, run.UnitPath);
        try
        {
            await SolutionFilter.WriteAsync(filter, solution, inSolution.Select(project => project.SolutionEntry!), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ProjectSelection.Everything($"the solution filter could not be written ({exception.Message})");
        }

        return new ProjectSelection(SelectionKind.Affected, "uncommitted changes", affected, entries.Count, filter, runsTests);
    }

    /// <summary>The solution the build runs: the <c>build:</c> file when it is one, else the target's only solution and no project beside it.</summary>
    private static (string? Solution, string Reason) SolutionOf(GateRunContext run)
    {
        if (run.BuildFile is string buildFile)
        {
            string path = Path.GetFullPath(buildFile, run.Target);
            return SolutionExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                ? (path, string.Empty)
                : (null, $"the build file {Path.GetFileName(path)} is not a solution");
        }

        string[] files = Directory.Exists(run.Target) ? [.. Directory.EnumerateFiles(run.Target)] : [];
        string[] solutions = [.. files.Where(file => SolutionExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))];
        bool projectBeside = files.Any(file => ProjectExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
        return solutions.Length == 1 && !projectBeside
            ? (solutions[0], string.Empty)
            : (null, solutions.Length == 0 && projectBeside ? "the target builds a single project" : "the target has no single solution file");
    }

    /// <summary>Null when the file is skipped or belongs to a project; else why it makes everything run.</summary>
    private static string? Classify(GateRunContext run, IReadOnlyList<GraphProject> projects, string file, Func<string, string> describe)
    {
        bool owned = Owners(projects, file).Any();
        if (!owned && run.UnitDirectories.Any(unit => AffectedPaths.IsUnder(file, unit) && !AffectedPaths.IsUnder(run.Target, unit)))
        {
            // Another unit's change: that unit judges it; a project of this one that uses it owns it.
            return null;
        }

        if (OwnDirectories.Any(directory => AffectedPaths.IsUnder(file, Path.Combine(run.ProjectRoot, directory))))
        {
            return null;
        }

        string name = Path.GetFileName(file);
        if (SharedInputNames.Contains(name) || SharedInputExtensions.Contains(Path.GetExtension(name)))
        {
            return $"{describe(file)} changed, a build input every project may share";
        }

        if (owned)
        {
            return null;
        }

        return AffectedPaths.IsUnder(file, run.Target) || BuildInputExtensions.Contains(Path.GetExtension(name))
            ? $"{describe(file)} changed outside every project, so which projects use it is not known"
            : null;
    }

    /// <summary>The projects a file changes: the one whose directory holds it most closely, and every one that includes it from outside.</summary>
    private static IEnumerable<GraphProject> Owners(IReadOnlyList<GraphProject> projects, string file)
    {
        GraphProject? holder = projects
            .Where(project => AffectedPaths.IsUnder(file, project.Directory))
            .MaxBy(project => project.Directory.Length);
        if (holder is not null)
        {
            yield return holder;
        }

        foreach (GraphProject project in projects.Where(project => project != holder && project.ExtraInputs.Any(input => AffectedPaths.IsUnder(file, input))))
        {
            yield return project;
        }
    }

    /// <summary>
    /// The changed projects and, breadth first, every project that references an affected one, each with its reason; in the
    /// graph's order (the solution's first).
    /// </summary>
    private static IReadOnlyList<AffectedProject> Affected(IReadOnlyList<GraphProject> projects, Dictionary<string, string> changedBy)
    {
        Dictionary<string, string> reasons = new(AffectedPaths.Comparer);
        Queue<GraphProject> pending = new();
        foreach (GraphProject project in projects.Where(project => changedBy.ContainsKey(project.File)))
        {
            reasons[project.File] = $"changed: {changedBy[project.File]}";
            pending.Enqueue(project);
        }

        Dictionary<string, string> origin = new(changedBy, AffectedPaths.Comparer);
        while (pending.TryDequeue(out GraphProject? reached))
        {
            foreach (GraphProject dependent in projects.Where(project => project.References.Contains(reached.File, AffectedPaths.Comparer) && !reasons.ContainsKey(project.File)))
            {
                origin[dependent.File] = origin[reached.File];
                reasons[dependent.File] = $"affected by {origin[reached.File]} via {reached.Name}";
                pending.Enqueue(dependent);
            }
        }

        return [.. projects.Where(project => reasons.ContainsKey(project.File))
            .Select(project => new AffectedProject(project.Name, project.SolutionEntry, reasons[project.File], project.DeclaresTests))];
    }
}
