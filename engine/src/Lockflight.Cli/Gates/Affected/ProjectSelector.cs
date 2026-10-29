using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// Chooses what the stop profile builds and tests in one solution: the projects the changes since the target's last stop PASS
/// touch (edits, commits, reverts, new and deleted files: <see cref="PassTree"/> against <see cref="StopPassRecord"/>) and,
/// transitively, every project that references one of them (<see cref="ProjectGraph"/>). Each changed file is, in this order: a
/// reason to run everything when it is a shared build input (<c>Directory.Build.*</c>, <c>Directory.Packages.props</c>,
/// <c>global.json</c>, <c>NuGet.config</c>, any <c>.props</c>/<c>.targets</c>, a solution, <c>.editorconfig</c>); a change to
/// every project whose directory holds it (a nested project's outer one too) and every project that includes it from outside;
/// skipped when it lies in another unit of the workspace or in Lockflight's own directories (<c>.lockflight/</c>,
/// <c>.claude/</c>, <c>.specify/</c>, <c>specs/</c>); else a reason to run everything when it lies in the target or is a source
/// or project file elsewhere. Everything runs, too, when anything is uncertain — no PASS recorded under these settings, no
/// single solution, a graph the project files cannot settle, git failing, nothing changed or nothing owned — so the choice fails
/// toward completeness. The pr profile and <c>gate run --full</c> always run everything.
/// </summary>
internal static class ProjectSelector
{
    /// <summary>The version of these rules, in the stop steps' cache key: a PASS chosen by older rules is not reused.</summary>
    public const int RulesVersion = 1;

    private const string PassExtension = ".pass";
    private const string FilterExtension = ".slnf";

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

    public static async Task<ProjectSelection> SelectAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        if (run.Profile != GateProfile.Stop)
        {
            return ProjectSelection.Everything("the pr profile runs every project");
        }

        if (await PassTree.RepositoryRootAsync(run.Target, cancellationToken) is not string repositoryRoot
            || await PassTree.CurrentAsync(repositoryRoot, cancellationToken) is not string tree)
        {
            return ProjectSelection.Everything("git could not read the working tree");
        }

        // The SDK the target resolves is part of the record: a new one (global.json rolls forward) may judge unchosen projects anew.
        StopPassRecord record = new(
            StopPassRecord.PathOf(run.ProjectRoot, run.Target, PassExtension), StopPassRecord.SettingsOf(run.ProjectRoot, await SdkAsync(run, cancellationToken)), tree);
        ProjectSelection selection;
        try
        {
            selection = await ChooseAsync(run, repositoryRoot, record, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            selection = ProjectSelection.Everything($"a project file could not be read ({exception.Message})");
        }

        return selection with { PassRecord = record };
    }

    private static async Task<ProjectSelection> ChooseAsync(GateRunContext run, string repositoryRoot, StopPassRecord record, CancellationToken cancellationToken)
    {
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

        if (await StopPassRecord.PassedTreeAsync(record.Location, record.Settings, cancellationToken) is not string passed)
        {
            return ProjectSelection.Everything("no stop PASS of this target, with this engine and these settings, to compare with");
        }

        if (await PassTree.ChangedAsync(repositoryRoot, passed, record.Tree, cancellationToken) is not IReadOnlyList<string> changes)
        {
            return ProjectSelection.Everything("git could not compare the tree with the one of the last PASS");
        }

        if (changes.Count == 0)
        {
            return ProjectSelection.Everything("nothing changed since the last PASS");
        }

        ProjectGraphRead graph = ProjectGraph.Read(solution, entries, repositoryRoot, Describe);
        if (graph.Problem is string problem)
        {
            return ProjectSelection.Everything(problem);
        }

        if (await PassTree.UnseenInputAsync(repositoryRoot, [.. graph.MsBuildFiles, .. graph.Projects.SelectMany(project => project.ExtraInputs)], Describe, cancellationToken) is string unseen)
        {
            return ProjectSelection.Everything(unseen);
        }

        Dictionary<string, string> changedBy = new(AffectedPaths.Comparer);
        foreach (string file in changes)
        {
            GraphProject[] owners = [.. Owners(graph.Projects, file)];
            if (Classify(run, owners.Length > 0, file, Describe) is string everything)
            {
                return ProjectSelection.Everything(everything);
            }

            foreach (GraphProject owner in owners)
            {
                changedBy.TryAdd(owner.File, Describe(file));
            }
        }

        if (changedBy.Count == 0)
        {
            return ProjectSelection.Everything("no project holds the changes since the last PASS");
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
            return ProjectSelection.Everything("no affected project declares tests, but a Directory.Build or Directory.Packages file names a test framework, so test projects cannot be told apart");
        }

        string filter = StopPassRecord.PathOf(run.ProjectRoot, run.Target, FilterExtension);
        await SolutionFilter.WriteAsync(filter, solution, inSolution.Select(project => project.SolutionEntry!), cancellationToken);
        return new ProjectSelection(SelectionKind.Affected, "changes since the last PASS", affected, entries.Count, filter, runsTests);
    }

    /// <summary>The SDK <c>dotnet</c> resolves in the target (its <c>global.json</c>), asked of the real tool; "none" without one.</summary>
    private static async Task<string> SdkAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        try
        {
            ProcessResult sdk = await ProcessRunner.RunAsync("dotnet", ["--version"], run.Target, run.TimeLeft(), cancellationToken);
            return $"{sdk.ExitCode}:{sdk.Output.Trim()}";
        }
        catch (ExecutableNotFoundException)
        {
            return "none";
        }
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
    private static string? Classify(GateRunContext run, bool owned, string file, Func<string, string> describe)
    {
        string name = Path.GetFileName(file);
        if (SharedInputNames.Contains(name) || SharedInputExtensions.Contains(Path.GetExtension(name)))
        {
            return $"{describe(file)} changed, a build input every project may share";
        }

        if (owned
            || run.UnitDirectories.Any(unit => AffectedPaths.IsUnder(file, unit) && !AffectedPaths.IsUnder(run.Target, unit))
            || OwnDirectories.Any(directory => AffectedPaths.IsUnder(file, Path.Combine(run.ProjectRoot, directory))))
        {
            // A project's change; another unit's (that unit judges it); or Lockflight's own state and specifications.
            return null;
        }

        return AffectedPaths.IsUnder(file, run.Target) || SharedChanges.SourceExtensions.Contains(Path.GetExtension(name))
            ? $"{describe(file)} changed outside every project, so which projects use it is not known"
            : null;
    }

    /// <summary>
    /// The projects a file changes: every one whose directory holds it (an outer project's default globs include a nested
    /// project's files, and projects can share a directory) and every one that includes it from outside.
    /// </summary>
    private static IEnumerable<GraphProject> Owners(IReadOnlyList<GraphProject> projects, string file) =>
        projects.Where(project => AffectedPaths.IsUnder(file, project.Directory) || project.ExtraInputs.Any(input => AffectedPaths.IsUnder(file, input)));

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
