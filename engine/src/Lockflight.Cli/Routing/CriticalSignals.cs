using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Paths the base branch's policy names: a path under <c>routing.critical</c> on either side of a move (<c>critical.path</c>), and
/// the policy files themselves (<c>critical.policy</c>: the engine's protected list, CODEOWNERS and required workflows). Both are
/// <b>boundaries</b>: the globs come from the base branch and the old path of every move is in the diff, so no change can dodge
/// them by path. Other CI files are <c>ci.workflowChanged</c> (a heuristic) unless the edit only bumps version pins. An edit of
/// <c>Directory.Packages.props</c> that only re-versions or adds packages is a dependency bump (<c>deps.changed</c>), not a policy
/// change; one that removes a package line (a package taken out, or swapped for another) is policy.
/// </summary>
internal sealed class CriticalSignals : IChangeSignalSource
{
    private const string CentralPackages = "Directory.Packages.props";

    public IEnumerable<ChangeSignal> Find(ChangeAnalysis analysis, RoutingPolicy policy)
    {
        foreach (RoutedFile file in analysis.Files)
        {
            if (policy.CriticalGlobOf(file.Path) is string critical)
            {
                yield return new ChangeSignal(SignalIds.CriticalPath, ChangeClass.Critical, SignalStrength.Boundary, file.Path, $"matches routing.critical {critical}");
            }

            if (policy.PolicyGlobOf(file.Path) is string glob && !IsCentralPackagesBump(analysis, file))
            {
                yield return new ChangeSignal(SignalIds.CriticalPolicy, ChangeClass.Critical, SignalStrength.Boundary, file.Path, $"policy file ({glob})");
            }
            else if (CiFiles.IsCiFile(file.Path) && ProjectSignals.CiPinsOnly(analysis, file) is null)
            {
                yield return new ChangeSignal(SignalIds.CiWorkflowChanged, ChangeClass.Feature, SignalStrength.Heuristic, file.Path, "CI definition changed");
            }
        }
    }

    /// <summary>Whether a change to <c>Directory.Packages.props</c> only re-versions or adds packages.</summary>
    public static bool IsCentralPackagesBump(ChangeAnalysis analysis, RoutedFile file) =>
        ProjectIndex.FileName(file.Path).Equals(CentralPackages, StringComparison.OrdinalIgnoreCase)
        && file.Change.Kind == ChangeKind.Modified
        && ProjectFileLines.OnlyVersionsOrAdditions(file.Added, file.Removed);
}
