using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// The routing settings of a unit repository of a workspace shell: the shell's settings (read from the shell's default branch)
/// with their globs rebased to the unit, merged with the unit's own <c>routing:</c> (read from the unit's base branch) by taking
/// the stricter value of every key: the union of glob lists, the higher class, the lower size limits, the higher coverage, the
/// earlier <c>*From</c> class. Neither side can loosen the other.
/// <list type="bullet">
/// <item>A shell glob under the unit's path (<c>services/orders/src/Payments/**</c>) becomes the unit's
/// (<c>src/Payments/**</c>); a glob that starts with <c>**/</c> applies everywhere; any other shell glob is about another
/// unit and is dropped.</item>
/// <item>Layers apply within one repository: the unit's own layers when it declares them, else the shell's, rebased.</item>
/// </list>
/// </summary>
internal static class UnitRoutingPolicy
{
    private const string AnyDirectory = "**/";

    public static RoutingSettings Merge(RoutingSettings shell, string unitPath, RoutingSettings? unit)
    {
        RoutingSettings rebased = shell with
        {
            Critical = Rebase(shell.Critical, unitPath),
            DbPaths = Rebase(shell.DbPaths, unitPath),
            GeneratedPaths = Rebase(shell.GeneratedPaths, unitPath),
            NonProduction = Rebase(shell.NonProduction, unitPath),
            RequiredWorkflows = Rebase(shell.RequiredWorkflows, unitPath),
            ApiProjects = Rebase(shell.ApiProjects, unitPath),
            Layers = [.. shell.Layers.Select(layer => layer with { Paths = Rebase(layer.Paths, unitPath) }).Where(layer => layer.Paths.Count > 0)],
        };
        if (unit is null || !unit.FromManifest)
        {
            return rebased;
        }

        int maxLines = Math.Min(rebased.FixMaxLines, unit.FixMaxLines);
        return rebased with
        {
            Floor = Max(rebased.Floor, unit.Floor),
            FixMaxLines = maxLines,
            FixMaxFiles = Math.Min(rebased.FixMaxFiles, unit.FixMaxFiles),
            LargeLines = Math.Max(maxLines + 1, Math.Min(rebased.LargeLines, unit.LargeLines)),
            Structural = Max(rebased.Structural, unit.Structural),
            Coverage = new RoutingCoverage(
                Math.Max(rebased.Coverage.Fix, unit.Coverage.Fix),
                Math.Max(rebased.Coverage.Behaviour, unit.Coverage.Behaviour),
                Math.Max(rebased.Coverage.Feature, unit.Coverage.Feature),
                Math.Max(rebased.Coverage.Critical, unit.Coverage.Critical)),
            Require = new RoutingRequirements(
                Min(rebased.Require.SpecFrom, unit.Require.SpecFrom),
                Min(rebased.Require.ReviewFrom, unit.Require.ReviewFrom),
                MinOrNever(rebased.Require.ReviewKeyFrom, unit.Require.ReviewKeyFrom),
                Min(rebased.Require.ArchitectureFrom, unit.Require.ArchitectureFrom),
                MinOrNever(rebased.Require.ArchitectureDeclaredFrom, unit.Require.ArchitectureDeclaredFrom)),
            Critical = Union(rebased.Critical, unit.Critical),
            DbPaths = Union(rebased.DbPaths, unit.DbPaths),
            GeneratedPaths = Intersect(rebased.GeneratedPaths, unit.GeneratedPaths),
            NonProduction = Intersect(rebased.NonProduction, unit.NonProduction),
            RequiredWorkflows = Union(rebased.RequiredWorkflows, unit.RequiredWorkflows),
            ApiProjects = Union(rebased.ApiProjects, unit.ApiProjects),
            Layers = unit.Layers.Count > 0 ? unit.Layers : rebased.Layers,
        };
    }

    private static List<string> Rebase(IReadOnlyList<string> globs, string unitPath)
    {
        string prefix = unitPath.Trim('/') + "/";
        return [.. globs
            .Select(glob => glob.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? glob[prefix.Length..] : glob.StartsWith(AnyDirectory, StringComparison.Ordinal) ? glob : null)
            .Where(glob => glob is { Length: > 0 })
            .Select(glob => glob!)
            .Distinct(StringComparer.Ordinal)];
    }

    private static List<string> Union(IReadOnlyList<string> first, IReadOnlyList<string> second) => [.. first.Concat(second).Distinct(StringComparer.Ordinal)];

    /// <summary>Exclusions only lower what counts, so the stricter merge keeps only the paths both sides exclude.</summary>
    private static List<string> Intersect(IReadOnlyList<string> first, IReadOnlyList<string> second) => [.. first.Intersect(second, StringComparer.Ordinal)];

    private static ChangeClass Max(ChangeClass first, ChangeClass second) => first > second ? first : second;

    private static ChangeClass Min(ChangeClass first, ChangeClass second) => first < second ? first : second;

    /// <summary>The earlier of two classes a requirement switches on at; null (never) only when both say never.</summary>
    private static ChangeClass? MinOrNever(ChangeClass? first, ChangeClass? second) =>
        first is ChangeClass a && second is ChangeClass b ? Min(a, b) : first ?? second;
}
