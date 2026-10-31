using Lockflight.Cli.Config;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Every way the pull request's <c>lockflight.yaml</c> is looser than the base's, compared setting by setting as the manifests read
/// (so a preset's filled-in values count like written ones): a gate mode lowered, a protected path or a critical, database or API
/// path removed, a path added to what routing leaves out, a routing threshold raised or a requirement starting at a higher class,
/// the approvals check or default weakened, the CI judge's limits widened or its test inputs grown, a unit removed, or a unit
/// repository's shell pointer removed or moved. The judge enforces the base's policy whatever the pull request says, so a looser
/// head is no bypass of this run; it is named because merging it weakens every later one, and the check FAILs so that a person,
/// not the agent, decides to merge it (through the platform, as for a finding). It is best effort: globs and layers are compared
/// as written (a rewritten glob, a layer's narrowed paths or a new layer order are not seen), and settings that only shape a run
/// (time limits, the review model and budget, the build file, the stop filter, whether red/green commits its reds) are not compared.
/// </summary>
internal static class PolicyLoosening
{
    /// <summary>The loosenings, one line each, in manifest order; empty when the head is as strict as the base, or stricter.</summary>
    public static IReadOnlyList<string> Between(Manifest baseManifest, Manifest head)
    {
        List<string> found = [];
        Removed(found, "protected", baseManifest.ProtectedGlobs, head.ProtectedGlobs);
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
        Removed(found, "routing.critical", before.Critical, after.Critical);
        Removed(found, "routing.dbPaths", before.DbPaths, after.DbPaths);
        Removed(found, "routing.apiProjects", before.ApiProjects, after.ApiProjects);
        Removed(found, "routing.requiredWorkflows", before.RequiredWorkflows, after.RequiredWorkflows);
        Added(found, "routing.generatedPaths", before.GeneratedPaths, after.GeneratedPaths);
        Added(found, "routing.nonProduction", before.NonProduction, after.NonProduction);
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
        Removed(found, "approvals.specFirstPaths", before.SpecFirstPaths, after.SpecFirstPaths);
        Removed(found, "approvals.specPaths", before.SpecPaths, after.SpecPaths);
    }

    private static void Ci(List<string> found, CiSettings before, CiSettings after)
    {
        Higher(found, "ci.maxRedCommits", before.MaxRedCommits, after.MaxRedCommits);
        Higher(found, "ci.flakyRetries", before.FlakyRetries, after.FlakyRetries);
        Added(found, "ci.lowRiskPaths", before.LowRiskPaths, after.LowRiskPaths);
        Added(found, "ci.testInputs", before.TestInputs, after.TestInputs);
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
        string[] removed = [.. before.Except(after, StringComparer.Ordinal)];
        if (removed.Length > 0)
        {
            found.Add($"{key}: removed {string.Join(", ", removed)}");
        }
    }

    private static void Added(List<string> found, string key, IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        string[] added = [.. after.Except(before, StringComparer.Ordinal)];
        if (added.Length > 0)
        {
            found.Add($"{key}: added {string.Join(", ", added)}");
        }
    }
}
