using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Workspace;

/// <summary>
/// The policy a unit repository's gates judge with: its own manifest made at least as strict as the workspace shell's policy for
/// it. The shell's protected paths that can match inside the unit are added, re-based as <c>workspace sync</c> re-bases them; the
/// build file and test framework the shell's entry names win over the unit's own (the unit's apply only where the shell names
/// none); of the coverage threshold and the switchable gate steps the stricter value wins; the shell's <c>routing:</c>, re-based to the
/// unit, is merged with the unit's own key by key, the stricter value winning (<see cref="UnitRoutingPolicy"/>). Nothing the unit's
/// manifest says can loosen the shell's policy.
/// </summary>
internal static class ShellPolicyOverlay
{
    public static Manifest Apply(Manifest unitManifest, ShellPolicy shell)
    {
        GateSettings own = unitManifest.Gates;
        GateSettings shells = shell.Manifest.Gates;
        string[] protectedGlobs = [.. unitManifest.ProtectedGlobs
            .Concat(shell.Manifest.ProtectedGlobs.SelectMany(glob => UnitPolicy.Rebase(glob, shell.Unit.Path)))
            .Distinct(StringComparer.Ordinal)];
        GateSettings gates = own with
        {
            TestFramework = shell.Unit.TestFramework ?? own.TestFramework ?? shells.TestFramework,
            Coverage = new CoverageSettings(
                own.Coverage.Collector == CoverageCollector.None ? shells.Coverage.Collector : own.Coverage.Collector,
                Math.Max(own.Coverage.PatchThreshold, shells.Coverage.PatchThreshold),
                Math.Min(own.Coverage.MinChangedLines, shells.Coverage.MinChangedLines)),
            BuildFile = shell.Unit.Build ?? own.BuildFile,
            Suppressions = Stricter(own.Suppressions, shells.Suppressions),
            DocsReferences = Stricter(own.DocsReferences, shells.DocsReferences),
            EfPendingModelChanges = Stricter(own.EfPendingModelChanges, shells.EfPendingModelChanges),
        };
        return unitManifest with
        {
            ProtectedGlobs = protectedGlobs,
            Gates = gates,
            Routing = UnitRoutingPolicy.Merge(shell.Manifest.Routing, shell.Unit.Path, unitManifest.Routing),
        };
    }

    /// <summary>The mode that judges more: <c>fail</c>, then <c>auto</c> (fails when it applies), then <c>report</c>, then <c>off</c>.</summary>
    public static GateMode Stricter(GateMode first, GateMode second) => Strictness(first) >= Strictness(second) ? first : second;

    private static int Strictness(GateMode mode) => mode switch
    {
        GateMode.Fail => 3,
        GateMode.Auto => 2,
        GateMode.Report => 1,
        _ => 0,
    };
}
