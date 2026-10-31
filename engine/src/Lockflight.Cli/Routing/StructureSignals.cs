using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Routing;

/// <summary>
/// The shape of the build: a new production project, a project that turns from production into a test project or back, a
/// build-wide file, a project added to or removed from a solution (each at <c>routing.structural</c> or <c>critical</c>), and
/// added project references: into a test project or against the declared layers (<c>critical</c>), otherwise
/// <c>structure.referenceAdded</c>: <c>behaviour</c> when both projects are in declared layers and it points inward, <c>feature</c>
/// otherwise. All heuristics.
/// </summary>
internal sealed partial class StructureSignals : IChangeSignalSource
{
    public IEnumerable<ChangeSignal> Find(ChangeAnalysis analysis, RoutingPolicy policy)
    {
        ChangeClass structural = policy.Settings.Structural;
        foreach (RoutedFile file in analysis.Files)
        {
            if (file.Move?.Kind == MoveKind.Exact)
            {
                continue;
            }

            if (ProjectIndex.IsProjectFile(file.Path))
            {
                foreach (ChangeSignal signal in ProjectFile(analysis, file, policy))
                {
                    yield return signal;
                }
            }
            else if (file.Role == FileRole.Project && file.Move?.OldPath != file.Path)
            {
                (IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed) = EffectiveLines.Of(analysis, file);
                if (added.Concat(removed).FirstOrDefault(line => SolutionProject().IsMatch(line.Text)) is RoutedLine project)
                {
                    yield return Signal(SignalIds.StructureSolution, structural, $"{file.Path}:{project.Line}", "a project added to or removed from the solution, or from its build");
                }
                else if (added.Concat(removed).Any(line => line.Text.Trim().Length > 0))
                {
                    yield return Signal(SignalIds.ProjectChanged, ChangeClass.Behaviour, file.Path, "solution file changed");
                }
            }
            else if (file.Role == FileRole.BuildWide && file.Move?.OldPath != file.Path && !OnlyDependencies(analysis, file))
            {
                yield return Signal(SignalIds.StructureBuildWide, structural, file.Path, "changes every project's build");
            }
        }

        foreach (ChangeSignal signal in KindChanges(analysis))
        {
            yield return signal;
        }
    }

    /// <summary>Whether a build-wide MSBuild file only re-versions or adds packages (those are <c>deps.changed</c>); removing one is build-wide.</summary>
    private static bool OnlyDependencies(ChangeAnalysis analysis, RoutedFile file)
    {
        if (file.Change.Kind != ChangeKind.Modified || !(file.Path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || file.Path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return ProjectFileLines.OnlyVersionsOrAdditions(file.Added, file.Removed);
    }

    private static IEnumerable<ChangeSignal> ProjectFile(ChangeAnalysis analysis, RoutedFile file, RoutingPolicy policy)
    {
        bool production = file.Change.ExistsAtHead && !analysis.HeadRoles.IsTestProject(file.Path);
        if (file.Change.Kind == ChangeKind.Added && file.Move is null && production)
        {
            yield return Signal(SignalIds.StructureProjectAdded, policy.Settings.Structural, file.Path, "new production project");
        }

        if (!production || file.Move?.OldPath == file.Path)
        {
            yield break;
        }

        string basePath = file.Move?.OldPath ?? file.Path;
        HashSet<string> before = new(
            analysis.ChangeSet.Base.TextOf(basePath) is string baseText ? ProjectReferences.Of(baseText, basePath).Select(Identity) : [],
            StringComparer.OrdinalIgnoreCase);
        if (analysis.ChangeSet.Head.TextOf(file.Path) is not string headText)
        {
            yield break;
        }

        foreach (ProjectReference reference in ProjectReferences.Of(headText, file.Path).Where(reference => !before.Contains(Identity(reference))))
        {
            yield return Reference(analysis, policy, file.Path, reference);
        }
    }

    /// <summary>What makes two references the same: the resolved target, or the text as written when it cannot be resolved.</summary>
    private static string Identity(ProjectReference reference) => reference.Target ?? "\u0001" + reference.Written;

    private static ChangeSignal Reference(ChangeAnalysis analysis, RoutingPolicy policy, string project, ProjectReference reference)
    {
        string where = $"{project}:{reference.Line}";
        string? target = reference.Target is string resolved ? HeadPath(analysis, resolved) : null;
        if (target is null)
        {
            return Signal(
                SignalIds.StructureReferenceAdded,
                policy.Settings.Structural,
                where,
                $"references {reference.Written}, which does not resolve to a project at head (an MSBuild property or a missing file)");
        }

        IReadOnlyList<RoutingLayer> layers = policy.Settings.Layers;
        if (analysis.HeadRoles.IsTestProject(target))
        {
            return Signal(SignalIds.StructureTestReference, ChangeClass.Critical, where, $"production project references test project {target}");
        }

        if (LayerOf(layers, project) is int from && LayerOf(layers, target) is int to && to > from)
        {
            return Signal(SignalIds.StructureLayerViolation, ChangeClass.Critical, where, $"layer {layers[from].Name} references outer layer {layers[to].Name} ({target})");
        }

        bool inward = LayerOf(layers, project) is int inner && LayerOf(layers, target) is int outer && outer <= inner;
        return Signal(
            SignalIds.StructureReferenceAdded,
            inward ? ChangeClass.Behaviour : ChangeClass.Feature,
            where,
            $"references {target}{(inward ? string.Empty : layers.Count > 0 ? "; outside the declared layers" : "; no routing.layers declared")}");
    }

    /// <summary>Projects whose test-or-production kind differs between base and head (their own text or a props file above them changed).</summary>
    private static IEnumerable<ChangeSignal> KindChanges(ChangeAnalysis analysis)
    {
        IEnumerable<string> projects = analysis.ChangeSet.Base.Paths.Where(ProjectIndex.IsProjectFile).Where(analysis.ChangeSet.Head.Contains);
        foreach (string project in projects.Order(StringComparer.Ordinal))
        {
            if (analysis.BaseRoles.IsTestProject(project) != analysis.HeadRoles.IsTestProject(project))
            {
                yield return Signal(
                    SignalIds.StructureProjectKindChanged,
                    ChangeClass.Critical,
                    project,
                    analysis.HeadRoles.IsTestProject(project) ? "production project becomes a test project" : "test project becomes a production project");
            }
        }

        foreach (FileMove move in analysis.Moves.Where(move => ProjectIndex.IsProjectFile(move.NewPath)))
        {
            if (analysis.BaseRoles.IsTestProject(move.OldPath) != analysis.HeadRoles.IsTestProject(move.NewPath))
            {
                yield return Signal(SignalIds.StructureProjectKindChanged, ChangeClass.Critical, move.NewPath, $"moved from {move.OldPath} and changed kind");
            }
        }
    }

    /// <summary>The head path of a referenced project, its spelling as the tree has it (names compared ignoring case); null when absent.</summary>
    private static string? HeadPath(ChangeAnalysis analysis, string target) =>
        analysis.ChangeSet.Head.Contains(target)
            ? target
            : analysis.ChangeSet.Head.Paths.Where(path => string.Equals(path, target, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).FirstOrDefault();

    private static int? LayerOf(IReadOnlyList<RoutingLayer> layers, string projectFile)
    {
        for (int index = 0; index < layers.Count; index++)
        {
            if (layers[index].Paths.Any(glob => PathRules.Matches(glob, projectFile)))
            {
                return index;
            }
        }

        return null;
    }

    private static ChangeSignal Signal(string id, ChangeClass raisesTo, string where, string detail) => new(id, raisesTo, SignalStrength.Heuristic, where, detail);

    [GeneratedRegex(@"^\s*(?:Project\(""|<Project\b[^>]*\bPath\s*=)|\.Build\.0\s*=|<Build\b|""path""\s*:|\.csproj""|\.fsproj""", RegexOptions.CultureInvariant)]
    private static partial Regex SolutionProject();
}
