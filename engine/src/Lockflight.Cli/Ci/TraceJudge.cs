using Lockflight.Cli.Config;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Reasons;
using Lockflight.Cli.Testing;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Trace in CI: every requirement of the spec must be named by a test that went red at one of this pull request's red commits, is
/// unchanged since (or its amend was accepted) and passes at head (<see cref="TraceMatrix"/>). Tests without a red never count.
/// When the spec was on the base already, the pull request answers for the requirements it adds or changes and those its red
/// tests name, so a feature can be delivered one story per pull request. A requirement marked <c>[outside .NET: …]</c> is excluded
/// when every file it names exists at head and names it (<see cref="OutsideVerification"/>); when every requirement is, trace is
/// N/A, never PASS. A requirement marked <c>[structural]</c> is covered by a structural test that passes at head and on the base's
/// code (<see cref="StructuralJudge"/>, the rule shared with local trace in <see cref="StructuralCheck"/>). In a unit repository of a workspace shell the spec is the
/// shell's (<see cref="ShellSpecSource"/>) and the pull request answers for the requirements its red tests name. A structural-only
/// change (<see cref="StructuralLane"/>) answers for every requirement of its spec, and adds no test that is not structural.
/// </summary>
internal static class TraceJudge
{
    public static async Task<TraceJudgement> DecideAsync(
        JudgePlan plan,
        IReadOnlyList<TestCase>? headInventory,
        IReadOnlyList<TestCase>? baseInventory,
        IReadOnlyList<CycleJudgement> cycles,
        IReadOnlyList<SinceRedTest>? sinceRed,
        GreenJudgement? green,
        IReadOnlyList<StructuralTest> structural,
        CancellationToken cancellationToken)
    {
        // Trace counts tests that pass at head: when green could not decide that, trace cannot either, and must not blame the tests.
        RedGreenTest[] accepted = [.. cycles.Where(cycle => cycle.Accepted).SelectMany(cycle => cycle.Tests)];
        if (accepted.Length > 0 && (green is null || accepted.Any(test => !green.AtHead.ContainsKey(test.Key))))
        {
            return new TraceJudgement(SpecResolution.None(null), CheckVerdict.NotEarned, ReasonCode.GreenUndecided, "green at head was not decided, so the tests that count for trace are not known", Applies: true, []);
        }

        if (accepted.Any(test => green!.AtHead.GetValueOrDefault(test.Key) == "flaky"))
        {
            return new TraceJudgement(SpecResolution.None(null), CheckVerdict.NotEarned, ReasonCode.Flaky, "a cycle test passed only when run again (FLAKY); trace waits for a stable green", Applies: true, []);
        }

        RedGreenTest[] counted = [.. cycles.Where(cycle => cycle.Accepted).SelectMany(cycle => cycle.Tests).Where(test => Counts(test, sinceRed, green))];
        string[] named = [.. counted.SelectMany(test => test.Requirements).Distinct(StringComparer.Ordinal)];
        SpecResolution spec = plan.Shell is ShellPolicy shell
            ? await ShellSpecSource.ResolveAsync(plan, shell, plan.Inputs.Branch, named, cancellationToken)
            : await TreeSpecSource.ResolveAsync(plan, plan.Inputs.Branch, named, cancellationToken);
        if (spec.FeatureId is null)
        {
            return spec.Problem is string problem
                ? new TraceJudgement(spec, CheckVerdict.NotEarned, ReasonCode.SpecUnresolved, problem, Applies: true, [])
                : new TraceJudgement(spec, CheckVerdict.NotApplicable, ReasonCode.None, "no spec found for this pull request", Applies: false, []);
        }

        if (spec.Problem is not null || spec.SpecText is not string text)
        {
            return new TraceJudgement(spec, CheckVerdict.NotEarned, ReasonCode.SpecUnresolved, spec.Problem ?? "the spec could not be read", Applies: true, []);
        }

        SpecDocument document = SpecDocument.Parse(text);
        IReadOnlyList<Requirement> requirements = document.Requirements;
        if (document.DuplicateMarkedIds.Count > 0)
        {
            return new TraceJudgement(spec, CheckVerdict.NotEarned, ReasonCode.SpecDuplicateRequirement, $"{spec.SpecPath} defines {string.Join(", ", document.DuplicateMarkedIds)} more than once, with a marker on one line; give each requirement one line", Applies: true, []);
        }

        if (requirements.Count == 0)
        {
            return new TraceJudgement(spec, CheckVerdict.NotEarned, ReasonCode.SpecNoRequirements, $"{spec.SpecPath} defines no requirements (FR-001 …); there is nothing to trace", Applies: true, []);
        }

        // A unit of a workspace shell delivers its part of a feature whose spec spans units: it answers for the requirements its
        // own red tests name.
        if (spec.Origin == SpecOrigin.Shell)
        {
            IReadOnlyList<Requirement> all = requirements;
            requirements = [.. requirements.Where(requirement => named.Contains(requirement.Id))];
            if (requirements.Count == 0 && named.Length > 0)
            {
                return new TraceJudgement(spec, CheckVerdict.NotEarned, ReasonCode.SpecRequirementUnknown,
                    $"the tests name {string.Join(", ", named)}, which the workspace shell's spec of {spec.FeatureId} does not define (it has {string.Join(", ", all.Select(requirement => requirement.Id))})", Applies: true, []);
            }

            if (requirements.Count == 0)
            {
                return plan.Class.Classification.Class >= ChangeClass.Feature
                    ? new TraceJudgement(spec, CheckVerdict.NotEarned, ReasonCode.TraceUncovered, $"no test that went red, then green names a requirement of {spec.FeatureId} in the workspace shell's spec; its class needs one", Applies: true, [])
                    : new TraceJudgement(spec, CheckVerdict.NotApplicable, ReasonCode.None, $"no test that went red, then green names a requirement of {spec.FeatureId} in the workspace shell's spec", Applies: false, []);
            }
        }

        // A feature delivered over several pull requests: this one answers for the requirements its spec diff adds or changes (text
        // or marker) and those its own red tests name; the others were traced when they were added. A structural-only change answers
        // for the whole spec: no red is needed because every requirement is covered here.
        IReadOnlyList<string> markerChanges = [];
        if (spec.Origin != SpecOrigin.Shell && plan.MergeBase is string mergeBase && spec.SpecPath is string specPath
            && await JudgeGit.ShowFileAsync(plan.Inputs.Root, mergeBase, specPath, cancellationToken) is string baseText)
        {
            IReadOnlyList<Requirement> baseRequirements = SpecDocument.Parse(baseText).Requirements;
            markerChanges = RequirementMarkers.ChangesSince(baseRequirements, requirements);
            Dictionary<string, Requirement> before = baseRequirements.DistinctBy(requirement => requirement.Id).ToDictionary(requirement => requirement.Id, StringComparer.Ordinal);
            requirements = plan.InStructuralLane ? requirements : [.. requirements.Where(requirement => !before.TryGetValue(requirement.Id, out Requirement? old)
                || old.Text != requirement.Text || old.Marker != requirement.Marker || named.Contains(requirement.Id))];
            if (requirements.Count == 0)
            {
                return new TraceJudgement(spec, CheckVerdict.NotApplicable, ReasonCode.None, $"no requirement of {spec.FeatureId} was added or changed in this pull request, and no red test names one", Applies: false, []);
            }
        }

        Dictionary<string, IReadOnlyList<OutsideFile>> outside = await OutsideAtHeadAsync(plan, requirements, cancellationToken);
        bool everyOutside = requirements.All(requirement => outside.TryGetValue(requirement.Id, out IReadOnlyList<OutsideFile>? files)
            && files.All(file => file.State == OutsideFileState.Names));
        if (headInventory is null && !(everyOutside && counted.Length == 0))
        {
            return new TraceJudgement(spec, CheckVerdict.NotEarned, ReasonCode.TestsNotListed, "the head's tests could not be listed to trace them", Applies: true, []) { MarkerChanges = markerChanges };
        }

        RedGreenRecord record = GreenJudge.Synthesized([], []) with
        {
            Cycles = [new RedGreenCycle(1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, counted)],
        };
        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(requirements, record, headInventory ?? [], outside, structural);
        TraceTally tally = TraceTally.Of(rows);
        string excluded = tally.Excluded > 0 ? $"; {tally.Excluded} verified outside .NET" : string.Empty;
        TraceJudgement judgement = tally.Outcome switch
        {
            TraceOutcome.Pass => new TraceJudgement(spec, CheckVerdict.Pass, ReasonCode.None, $"{tally.Covered}/{rows.Count} requirements of {spec.FeatureId}{excluded}", Applies: true, rows),
            // Applies, so a class that would need trace gets N/A as a required check (NOT-EARNED), never a pass by omission.
            TraceOutcome.NotApplicable => new TraceJudgement(spec, CheckVerdict.NotApplicable, ReasonCode.None,
                $"every requirement of {spec.FeatureId} this pull request answers for ({rows.Count}) is verified outside .NET by the files its marker names; Lockflight runs none of those tests", Applies: true, rows),
            _ => new TraceJudgement(spec, CheckVerdict.Fail, ReasonCode.TraceUncovered,
                $"{tally.Covered}/{rows.Count} requirements of {spec.FeatureId} covered{excluded}; not covered by a test that went red, then green: {string.Join(", ", rows.Where(row => tally.Uncovered.Contains(row.RequirementId)).Select(Uncovered))}", Applies: true, rows),
        };
        judgement = judgement with { MarkerChanges = markerChanges, Structural = structural };
        return plan.InStructuralLane && judgement.Verdict == CheckVerdict.Pass ? NewTestsOfStructuralChange(judgement, spec, headInventory ?? [], baseInventory, requirements) : judgement;
    }

    /// <summary>
    /// A structural-only change adds only structural tests: a new test at head that names no structural requirement of the spec pins
    /// new behaviour, which needs a red commit, so trace is NOT-EARNED.
    /// </summary>
    private static TraceJudgement NewTestsOfStructuralChange(
        TraceJudgement judgement, SpecResolution spec, IReadOnlyList<TestCase> headInventory, IReadOnlyList<TestCase>? baseInventory, IReadOnlyList<Requirement> requirements)
    {
        if (baseInventory is null)
        {
            return judgement with { Verdict = CheckVerdict.NotEarned, Reason = ReasonCode.BaseNotCompared, Why = "the base's tests could not be listed, so the tests a structural-only change adds are not known" };
        }

        IReadOnlySet<string> structuralIds = StructuralCheck.Ids(requirements);
        HashSet<string> onBase = [.. baseInventory.Select(test => test.Key)];
        string[] plain = [.. headInventory.Where(test => !onBase.Contains(test.Key) && !StructuralCheck.IsStructural(test.Requirements, structuralIds))
            .Select(test => TraceMatrix.ShortName(test.MethodFilter)).Distinct(StringComparer.Ordinal)];
        return plain.Length == 0
            ? judgement with { Why = $"{judgement.Why} (structural-only change)" }
            : judgement with
            {
                Verdict = CheckVerdict.NotEarned,
                Reason = ReasonCode.StructuralChangeNewTest,
                Why = $"a structural-only change adds only structural tests, and {plain.Length} new tests name no structural requirement of {spec.FeatureId}: "
                    + $"{string.Join(", ", plain.Take(3))}{(plain.Length > 3 ? " …" : string.Empty)}; a new test pins new behaviour, which needs a red commit",
            };
    }

    private static string Uncovered(TraceRow row) => row.Note is string problem ? $"{row.RequirementId} ({problem})" : row.RequirementId;

    /// <summary>
    /// The files of each requirement marked <c>[outside .NET: …]</c> as they are at head, each with whether the pull request added or
    /// changed it.
    /// </summary>
    private static async Task<Dictionary<string, IReadOnlyList<OutsideFile>>> OutsideAtHeadAsync(JudgePlan plan, IReadOnlyList<Requirement> requirements, CancellationToken cancellationToken)
    {
        Dictionary<string, IReadOnlyList<OutsideFile>> outside = new(StringComparer.Ordinal);
        foreach (Requirement requirement in requirements.Where(requirement => requirement.VerifiedOutside.Count > 0).DistinctBy(requirement => requirement.Id))
        {
            List<OutsideFile> files = [];
            foreach (string path in requirement.VerifiedOutside)
            {
                if (!OutsideVerification.IsInsideProject(path))
                {
                    files.Add(OutsideVerification.Judge(requirement.Id, path, null));
                    continue;
                }

                string gitPath = path.Replace('\\', '/');
                string? content = !OutsideVerification.IsInSpecs(path) && await JudgeGit.IsRegularFileAsync(plan.Inputs.Root, plan.Inputs.Head, gitPath, cancellationToken)
                    ? await JudgeGit.ShowFileAsync(plan.Inputs.Root, plan.Inputs.Head, gitPath, cancellationToken)
                    : null;
                files.Add(OutsideVerification.Judge(requirement.Id, path, content) with { Change = await ChangeAsync(plan, gitPath, cancellationToken) });
            }

            outside[requirement.Id] = files;
        }

        return outside;
    }

    private static async Task<string?> ChangeAsync(JudgePlan plan, string path, CancellationToken cancellationToken)
    {
        if (await JudgeGit.BlobOfAsync(plan.Inputs.Root, plan.Inputs.Head, path, cancellationToken) is not string atHead || plan.MergeBase is not string mergeBase)
        {
            return null;
        }

        return await JudgeGit.BlobOfAsync(plan.Inputs.Root, mergeBase, path, cancellationToken) is not string onBase ? "added in this pull request"
            : onBase == atHead ? "unchanged in this pull request"
            : "changed in this pull request";
    }

    /// <summary>A cycle test counts when it is still the test that went red and passes at head.</summary>
    private static bool Counts(RedGreenTest test, IReadOnlyList<SinceRedTest>? sinceRed, GreenJudgement? green)
    {
        SinceRedTest? since = sinceRed?.FirstOrDefault(entry => entry.Key == test.Key);
        bool stillRed = since is null ? sinceRed is null : since.State is SinceRed.Unchanged or SinceRed.AmendAccepted;
        return stillRed && green?.AtHead.GetValueOrDefault(test.Key) == "pass";
    }
}
