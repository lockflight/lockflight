using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Trace in CI: every requirement of the spec must be named by a test that went red at one of this pull request's red commits, is
/// unchanged since (or its amend was accepted) and passes at head (<see cref="TraceMatrix"/>). Tests without a red never count.
/// </summary>
internal static class TraceJudge
{
    public static async Task<TraceJudgement> DecideAsync(
        JudgePlan plan, IReadOnlyList<TestCase>? headInventory, IReadOnlyList<CycleJudgement> cycles, IReadOnlyList<SinceRedTest>? sinceRed, GreenJudgement? green, CancellationToken cancellationToken)
    {
        RedGreenTest[] counted = [.. cycles.Where(cycle => cycle.Accepted).SelectMany(cycle => cycle.Tests).Where(test => Counts(test, sinceRed, green))];
        SpecResolution spec = await TreeSpecSource.ResolveAsync(plan, plan.Inputs.Branch, [.. counted.SelectMany(test => test.Requirements)], cancellationToken);
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
