using System.Xml;
using Lockflight.Cli.Config;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Project files and dependencies: package and version edits (<c>deps.changed</c>, also for lock files and version-only pin bumps
/// in CI files), other project edits (<c>project.changed</c>), properties that change what the build enforces or compiles
/// (<c>project.policyProperty</c>, at <c>routing.structural</c>), and project files that cannot be read (<c>project.unreadable</c>).
/// References between projects are the structure signals' (<see cref="StructureSignals"/>). All heuristics.
/// </summary>
internal sealed class ProjectSignals : IChangeSignalSource
{
    public IEnumerable<ChangeSignal> Find(ChangeAnalysis analysis, RoutingPolicy policy)
    {
        foreach (RoutedFile file in analysis.Files)
        {
            if (file.Move?.Kind == MoveKind.Exact)
            {
                continue;
            }

            if (file.Role == FileRole.Generated && GeneratedFiles.IsGenerated(file.Path, content: null, _ => false))
            {
                yield return Signal(SignalIds.DepsChanged, ChangeClass.Behaviour, file.Path, "package lock file changed");
            }
            else if (ProjectIndex.IsProjectFile(file.Path))
            {
                foreach (ChangeSignal signal in ProjectFile(analysis, file, policy))
                {
                    yield return signal;
                }
            }
            else if (file.Role == FileRole.BuildWide && IsMsBuildFile(file.Path) && file.Move?.OldPath != file.Path
                && ProjectFileLines.Of(file.Added, file.Removed).FirstOrDefault(line => line.Kind == ProjectLineKind.Package) is ProjectLine package)
            {
                yield return Signal(SignalIds.DepsChanged, ChangeClass.Behaviour, $"{file.Path}:{package.Line.Line}", "package reference or version changed");
            }
            else if (CiFiles.IsCiFile(file.Path) && CiPinsOnly(analysis, file) is RoutedLine pin)
            {
                yield return Signal(SignalIds.DepsChanged, ChangeClass.Behaviour, $"{file.Path}:{pin.Line}", "version-only pin edit");
            }
        }
    }

    /// <summary>Whether every changed line of a CI file is one half of a version-only pin edit; the first added such line, or null.</summary>
    public static RoutedLine? CiPinsOnly(ChangeAnalysis analysis, RoutedFile file)
    {
        (IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed) = EffectiveLines.Of(analysis, file);
        List<RoutedLine> addedCode = [.. added.Where(line => line.Text.Trim().Length > 0)];
        List<RoutedLine> removedCode = [.. removed.Where(line => line.Text.Trim().Length > 0)];
        HashSet<RoutedLine> pins = ProjectFileLines.Pairs(addedCode, removedCode, CiFiles.WithoutPins);
        return addedCode.Count > 0 && pins.Count == addedCode.Count + removedCode.Count ? addedCode[0] : null;
    }

    private static IEnumerable<ChangeSignal> ProjectFile(ChangeAnalysis analysis, RoutedFile file, RoutingPolicy policy)
    {
        if (Unreadable(analysis, file) is string why)
        {
            yield return Signal(SignalIds.ProjectUnreadable, ChangeClass.Behaviour, file.Path, why);
        }

        if (file.Change.Kind == ChangeKind.Added && file.Move is null)
        {
            yield break;
        }

        if (file.Change.Kind == ChangeKind.Deleted && file.Move is null)
        {
            yield return Signal(SignalIds.ProjectChanged, ChangeClass.Behaviour, file.Path, "project file deleted");
            yield break;
        }

        (IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed) = EffectiveLines.Of(analysis, file);
        IReadOnlyList<ProjectLine> lines = ProjectFileLines.Of(added, removed);
        if (lines.FirstOrDefault(line => line.Kind == ProjectLineKind.Package) is ProjectLine package)
        {
            yield return Signal(SignalIds.DepsChanged, ChangeClass.Behaviour, $"{file.Path}:{package.Line.Line}", "package reference or version changed");
        }

        if (lines.FirstOrDefault(line => line.Kind == ProjectLineKind.PolicyProperty) is ProjectLine property)
        {
            yield return Signal(SignalIds.ProjectPolicyProperty, policy.Settings.Structural, $"{file.Path}:{property.Line.Line}", $"sets {Trimmed(property.Line.Text)}");
        }

        if (lines.FirstOrDefault(line => line.Kind == ProjectLineKind.Other || (line.Kind == ProjectLineKind.Reference && !line.Added)) is ProjectLine other)
        {
            yield return Signal(SignalIds.ProjectChanged, ChangeClass.Behaviour, $"{file.Path}:{other.Line.Line}", $"{(other.Added ? "adds" : "removes")} {Trimmed(other.Line.Text)}");
        }
    }

    /// <summary>Why a project file cannot be read on a side where it exists (no text, or not well-formed XML); null when it can.</summary>
    private static string? Unreadable(ChangeAnalysis analysis, RoutedFile file)
    {
        foreach ((bool exists, RevisionSnapshot side, string name) in new[] { (file.Change.ExistsAtBase, analysis.ChangeSet.Base, "base"), (file.Change.ExistsAtHead, analysis.ChangeSet.Head, "head") })
        {
            if (!exists)
            {
                continue;
            }

            if (side.TextOf(file.Path) is not string text)
            {
                return $"cannot be read at {name}";
            }

            if (!IsWellFormed(text))
            {
                return $"not well-formed XML at {name}";
            }
        }

        return null;
    }

    private static bool IsWellFormed(string text)
    {
        try
        {
            using XmlReader reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read())
            {
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool IsMsBuildFile(string path) =>
        path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);

    private static string Trimmed(string text)
    {
        const int MaxLength = 80;
        string collapsed = WhitespacePairs.Collapse(text);
        return collapsed.Length <= MaxLength ? collapsed : collapsed[..MaxLength] + "…";
    }

    private static ChangeSignal Signal(string id, ChangeClass raisesTo, string where, string detail) => new(id, raisesTo, SignalStrength.Heuristic, where, detail);
}
