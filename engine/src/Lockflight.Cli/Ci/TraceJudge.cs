using Lockflight.Cli.Config;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Trace in CI: every requirement of the spec must be named by a test that went red at one of this pull request's red commits, is
/// unchanged since (or its amend was accepted) and passes at head (<see cref="TraceMatrix"/>). Tests without a red never count.
/// When the spec was on the base already, the pull request answers for the requirements it adds or changes and those its red
/// tests name, so a feature can be delivered one story per pull request. In a unit repository of a workspace shell the spec is the
/// shell's (<see cref="ShellSpecSource"/>) and the pull request answers for the requirements its red tests name.
/// </summary>
internal static class TraceJudge
{
    public static async Task<TraceJudgement> DecideAsync(
        JudgePlan plan, IReadOnlyList<TestCase>? headInventory, IReadOnlyList<CycleJudgement> cycles, IReadOnlyList<SinceRedTest>? sinceRed, GreenJudgement? green, CancellationToken cancellationToken)
    {
        // Trace counts tests that pass at head: when green could not decide that, trace cannot either, and must not blame the tests.
        RedGreenTest[] accepted = [.. cycles.Where(cycle => cycle.Accepted).SelectMany(cycle => cycle.Tests)];
        if (accepted.Length > 0 && (green is null || accepted.Any(test => !green.AtHead.ContainsKey(test.Key))))
        {
            return new TraceJudgement(SpecResolution.None(null), CheckVerdict.NotEarned, "green at head was not decided, so the tests that count for trace are not known", Applies: true, []);
        }

        if (accepted.Any(test => green!.AtHead.GetValueOrDefault(test.Key) == "flaky"))
        {
            return new TraceJudgement(SpecResolution.None(null), CheckVerdict.NotEarned, "a cycle test passed only when run again (FLAKY); trace waits for a stable green", Applies: true, []);
        }

        RedGreenTest[] counted = [.. cycles.Where(cycle => cycle.Accepted).SelectMany(cycle => cycle.Tests).Where(test => Counts(test, sinceRed, green))];
        string[] named = [.. counted.SelectMany(test => test.Requirements).Distinct(StringComparer.Ordinal)];
        SpecResolution spec = plan.Shell is ShellPolicy shell
            ? await ShellSpecSource.ResolveAsync(plan, shell, plan.Inputs.Branch, named, cancellationToken)
            : await TreeSpecSource.ResolveAsync(plan, plan.Inputs.Branch, named, cancellationToken);
        if (spec.FeatureId is null)
        {
            return spec.Problem is string problem
                ? new TraceJudgement(spec, CheckVerdict.NotEarned, problem, Applies: true, [])
                : new TraceJudgement(spec, CheckVerdict.NotApplicable, "no spec found for this pull request", Applies: false, []);
        }

        if (spec.Problem is not null || spec.SpecText is not string text)
        {
            return new TraceJudgement(spec, CheckVerdict.NotEarned, spec.Problem ?? "the spec could not be read", Applies: true, []);
        }

        IReadOnlyList<Requirement> requirements = SpecDocument.Parse(text).Requirements;
        if (requirements.Count == 0)
        {
            return new TraceJudgement(spec, CheckVerdict.NotEarned, $"{spec.SpecPath} defines no requirements (FR-001 …); there is nothing to trace", Applies: true, []);
        }

        // A unit of a workspace shell delivers its part of a feature whose spec spans units: it answers for the requirements its
        // own red tests name.
        if (spec.Origin == SpecOrigin.Shell)
        {
            IReadOnlyList<Requirement> all = requirements;
            requirements = [.. requirements.Where(requirement => named.Contains(requirement.Id))];
            if (requirements.Count == 0 && named.Length > 0)
            {
                return new TraceJudgement(spec, CheckVerdict.NotEarned,
                    $"the tests name {string.Join(", ", named)}, which the workspace shell's spec of {spec.FeatureId} does not define (it has {string.Join(", ", all.Select(requirement => requirement.Id))})", Applies: true, []);
            }

            if (requirements.Count == 0)
            {
                return plan.Class.Classification.Class >= ChangeClass.Feature
                    ? new TraceJudgement(spec, CheckVerdict.NotEarned, $"no test that went red, then green names a requirement of {spec.FeatureId} in the workspace shell's spec; its class needs one", Applies: true, [])
                    : new TraceJudgement(spec, CheckVerdict.NotApplicable, $"no test that went red, then green names a requirement of {spec.FeatureId} in the workspace shell's spec", Applies: false, []);
            }
        }

        // A feature delivered over several pull requests: this one answers for the requirements its spec diff adds or changes and
        // those its own red tests name; the others were traced when they were added.
        else if (plan.MergeBase is string mergeBase && spec.SpecPath is string specPath
            && await JudgeGit.ShowFileAsync(plan.Inputs.Root, mergeBase, specPath, cancellationToken) is string baseText)
        {
            Dictionary<string, string> before = SpecDocument.Parse(baseText).Requirements.GroupBy(requirement => requirement.Id).ToDictionary(group => group.Key, group => group.First().Text, StringComparer.Ordinal);
            requirements = [.. requirements.Where(requirement => !before.TryGetValue(requirement.Id, out string? old) || old != requirement.Text || named.Contains(requirement.Id))];
            if (requirements.Count == 0)
            {
                return new TraceJudgement(spec, CheckVerdict.NotApplicable, $"no requirement of {spec.FeatureId} was added or changed in this pull request, and no red test names one", Applies: false, []);
            }
        }

        if (headInventory is null)
        {
            return new TraceJudgement(spec, CheckVerdict.NotEarned, "the head's tests could not be listed to trace them", Applies: true, []);
        }

        RedGreenRecord record = GreenJudge.Synthesized([], []) with
        {
            Cycles = [new RedGreenCycle(1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, counted)],
        };
        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(requirements, record, headInventory);
        string[] uncovered = [.. rows.Where(row => !row.Covered).Select(row => row.RequirementId)];
        int covered = rows.Count - uncovered.Length;
        return uncovered.Length == 0
            ? new TraceJudgement(spec, CheckVerdict.Pass, $"{covered}/{rows.Count} requirements of {spec.FeatureId}", Applies: true, rows)
            : new TraceJudgement(spec, CheckVerdict.Fail, $"{covered}/{rows.Count} requirements of {spec.FeatureId} covered; not covered by a test that went red, then green: {string.Join(", ", uncovered)}", Applies: true, rows);
    }

    /// <summary>A cycle test counts when it is still the test that went red and passes at head.</summary>
    private static bool Counts(RedGreenTest test, IReadOnlyList<SinceRedTest>? sinceRed, GreenJudgement? green)
    {
        SinceRedTest? since = sinceRed?.FirstOrDefault(entry => entry.Key == test.Key);
        bool stillRed = since is null ? sinceRed is null : since.State is SinceRed.Unchanged or SinceRed.AmendAccepted;
        return stillRed && green?.AtHead.GetValueOrDefault(test.Key) == "pass";
    }
}
