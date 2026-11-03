using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Every way the pull request's <c>lockflight.yaml</c> is looser than the base's, compared setting by setting as the manifests read
/// (so a preset's filled-in values count like written ones): a gate mode lowered, a protected path or a critical, database or API
/// path removed, a path added to what routing leaves out, a routing threshold raised or a requirement starting at a higher class,
/// the approvals check or default weakened, the CI judge's limits widened or its test inputs grown, a unit removed, or a unit
/// repository's shell pointer removed or moved. The judge enforces the base's policy whatever the pull request says, so a looser
/// head is no bypass of this run; it is named because merging it weakens every later one, and the check FAILs so that a person,
/// not the agent, decides to merge it (through the platform, as for a finding). A glob is compared by what it matches: one removed
/// from a list is no loosening when a single glob left still matches every path it did (<c>src/Payments/**</c> under
/// <c>src/**</c>), and one added is none when a single glob already there did; a glob the comparison does not model counts as
/// written (<see cref="GlobContainment"/>). It is best effort: globs are not combined (two narrower globs that together cover a
/// removed one still name it), layers are compared by name (a layer's narrowed paths or a new layer order are not seen), and settings that only shape a run
/// (time limits, the review model and budget, the build file, the stop filter, whether red/green commits its reds) are not compared.
/// </summary>
internal static class PolicyLoosening
{
    /// <summary>The loosenings, one line each, in manifest order; empty when the head is as strict as the base, or stricter.</summary>
    public static IReadOnlyList<string> Between(Manifest baseManifest, Manifest head)
    {
        List<string> found = [];
        RemovedGlobs(found, "protected", baseManifest.ProtectedGlobs, head.ProtectedGlobs);
        Removed(found, "units", [.. baseManifest.Units.Select(unit => unit.Path)], [.. head.Units.Select(unit => unit.Path)]);
        Shell(found, baseManifest.Shell, head.Shell);
        if (baseManifest.StopGate && !head.StopGate)
        {
            found.Add("stop.gate: true → false");
        }

        Gates(found, baseManifest.Gates, head.Gates);
        Routing(found, baseManifest.Routing, head.Routing);
        Approvals(found, baseManifest.Approvals, head.Approvals);
        Ci(found, baseManifest.Ci, head.Ci);
        return found;
    }

    private static void Gates(List<string> found, GateSettings before, GateSettings after)
    {
        Mode(found, "gates.quality.suppressions", before.Suppressions, after.Suppressions);
        Mode(found, "gates.docs.references", before.DocsReferences, after.DocsReferences);
        Mode(found, "gates.ef.pendingModelChanges", before.EfPendingModelChanges, after.EfPendingModelChanges);
        if (before.Coverage.Collector != CoverageCollector.None && after.Coverage.Collector == CoverageCollector.None)
        {
            found.Add($"gates.coverage.collector: {before.Coverage.Collector.ToString().ToLowerInvariant()} → none");
        }

        Lower(found, "gates.coverage.patchThreshold", before.Coverage.PatchThreshold, after.Coverage.PatchThreshold);
        Higher(found, "gates.coverage.minChangedLines", before.Coverage.MinChangedLines, after.Coverage.MinChangedLines);
    }

    private static void Routing(List<string> found, RoutingSettings before, RoutingSettings after)
    {
        Lowered(found, "routing.floor", before.Floor, after.Floor);
        Higher(found, "routing.fix.maxLines", before.FixMaxLines, after.FixMaxLines);
        Higher(found, "routing.fix.maxFiles", before.FixMaxFiles, after.FixMaxFiles);
        Higher(found, "routing.largeLines", before.LargeLines, after.LargeLines);
        Lowered(found, "routing.structural", before.Structural, after.Structural);
        Lower(found, "routing.coverage.fix", before.Coverage.Fix, after.Coverage.Fix);
        Lower(found, "routing.coverage.behaviour", before.Coverage.Behaviour, after.Coverage.Behaviour);
        Lower(found, "routing.coverage.feature", before.Coverage.Feature, after.Coverage.Feature);
        Lower(found, "routing.coverage.critical", before.Coverage.Critical, after.Coverage.Critical);
        Raised(found, "routing.require.specFrom", before.Require.SpecFrom, after.Require.SpecFrom);
        Raised(found, "routing.require.reviewFrom", before.Require.ReviewFrom, after.Require.ReviewFrom);
        From(found, "routing.require.reviewKeyFrom", before.Require.ReviewKeyFrom, after.Require.ReviewKeyFrom);
        Raised(found, "routing.require.architectureFrom", before.Require.ArchitectureFrom, after.Require.ArchitectureFrom);
        From(found, "routing.require.architectureDeclaredFrom", before.Require.ArchitectureDeclaredFrom, after.Require.ArchitectureDeclaredFrom);
        RemovedGlobs(found, "routing.critical", before.Critical, after.Critical);
        RemovedGlobs(found, "routing.dbPaths", before.DbPaths, after.DbPaths);
        RemovedGlobs(found, "routing.apiProjects", before.ApiProjects, after.ApiProjects);
        RemovedGlobs(found, "routing.requiredWorkflows", before.RequiredWorkflows, after.RequiredWorkflows);
        AddedGlobs(found, "routing.generatedPaths", before.GeneratedPaths, after.GeneratedPaths);
        AddedGlobs(found, "routing.nonProduction", before.NonProduction, after.NonProduction);
        Removed(found, "routing.layers", [.. before.Layers.Select(layer => layer.Name)], [.. after.Layers.Select(layer => layer.Name)]);
    }

    private static void Approvals(List<string> found, ApprovalSettings before, ApprovalSettings after)
    {
        if (before.Check == ApprovalCheckMode.Fail && after.Check != ApprovalCheckMode.Fail)
        {
            found.Add("approvals.check: fail → report");
        }

        if (before.Mode == ApprovalMode.SpecFirst && after.Mode != ApprovalMode.SpecFirst)
        {
            found.Add("approvals.default: spec-first → same-pr");
        }

        From(found, "approvals.specFirstFrom", before.SpecFirstFrom, after.SpecFirstFrom);
        RemovedGlobs(found, "approvals.specFirstPaths", before.SpecFirstPaths, after.SpecFirstPaths);
        RemovedGlobs(found, "approvals.specPaths", before.SpecPaths, after.SpecPaths);
    }

    private static void Ci(List<string> found, CiSettings before, CiSettings after)
    {
        Higher(found, "ci.maxRedCommits", before.MaxRedCommits, after.MaxRedCommits);
        Higher(found, "ci.flakyRetries", before.FlakyRetries, after.FlakyRetries);
        AddedGlobs(found, "ci.lowRiskPaths", before.LowRiskPaths, after.LowRiskPaths);
        AddedGlobs(found, "ci.testInputs", before.TestInputs, after.TestInputs);
    }

    /// <summary>A unit repository's pointer to its workspace shell, whose stricter policy and specs the judge adds: gone or moved.</summary>
    private static void Shell(List<string> found, ShellSettings? before, ShellSettings? after)
    {
        if (before is null || before == after)
        {
            return;
        }

        found.Add(after is null ? "shell: removed" : $"shell: now {after.Url} {after.Ref ?? "(default branch)"} unit {after.Unit}");
    }

    private static void Mode(List<string> found, string key, GateMode before, GateMode after)
    {
        if (Strictness(after) < Strictness(before))
        {
            found.Add($"{key}: {GateModeNames.ToName(before)} → {GateModeNames.ToName(after)}");
        }
    }

    /// <summary>fail, then auto (fails where it applies), then report, then off.</summary>
    private static int Strictness(GateMode mode) => mode switch
    {
        GateMode.Fail => 3,
        GateMode.Auto => 2,
        GateMode.Report => 1,
        _ => 0,
    };

    private static void Lowered(List<string> found, string key, ChangeClass before, ChangeClass after)
    {
        if (after < before)
        {
            found.Add($"{key}: {ChangeClassNames.ToName(before)} → {ChangeClassNames.ToName(after)}");
        }
    }

    private static void Raised(List<string> found, string key, ChangeClass before, ChangeClass after)
    {
        if (after > before)
        {
            found.Add($"{key}: {ChangeClassNames.ToName(before)} → {ChangeClassNames.ToName(after)}");
        }
    }

    /// <summary>A requirement that starts at a class, or never (null): a later start, or none, is looser.</summary>
    private static void From(List<string> found, string key, ChangeClass? before, ChangeClass? after)
    {
        if (before is ChangeClass was && (after is null || after > was))
        {
            found.Add($"{key}: {ChangeClassNames.ToName(was)} → {(after is ChangeClass now ? ChangeClassNames.ToName(now) : "never")}");
        }
    }

    private static void Higher(List<string> found, string key, int before, int after)
    {
        if (after > before)
        {
            found.Add($"{key}: {before} → {after}");
        }
    }

    private static void Lower(List<string> found, string key, int before, int after)
    {
        if (after < before)
        {
            found.Add($"{key}: {before} → {after}");
        }
    }

    private static void Removed(List<string> found, string key, IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        // One line per path, so a long list stays readable line by line in the comment and the evidence.
        found.AddRange(before.Except(after, StringComparer.Ordinal).Select(path => $"{key}: removed {path}"));
    }

    /// <summary>A glob gone from a list that tightens as it grows, unless a glob left in it still matches every path it matched.</summary>
    private static void RemovedGlobs(List<string> found, string key, IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        found.AddRange(before.Distinct(StringComparer.Ordinal).Where(glob => !after.Any(kept => GlobContainment.Covers(kept, glob)))
            .Select(glob => $"{key}: removed {glob}"));
    }

    /// <summary>A glob new in a list that loosens as it grows, unless a glob already in it matched every path it matches.</summary>
    private static void AddedGlobs(List<string> found, string key, IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        found.AddRange(after.Distinct(StringComparer.Ordinal).Where(glob => !before.Any(had => GlobContainment.Covers(had, glob)))
            .Select(glob => $"{key}: added {glob}"));
    }
}
