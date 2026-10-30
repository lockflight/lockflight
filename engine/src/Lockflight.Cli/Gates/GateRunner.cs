using Lockflight.Cli.Gates.Affected;

namespace Lockflight.Cli.Gates;

/// <summary>
/// Runs a profile's steps in order (<see cref="GateProfiles"/>) in a single repository and stops at the first step that does not
/// pass, so the agent gets the most actionable failure first.
/// </summary>
internal static class GateRunner
{
    public static async Task<GateResult> RunAsync(GateRunContext run, TextWriter progress, CancellationToken cancellationToken)
    {
        List<GateStep> steps = [];
        GateRunFacts facts = GateRunFacts.None;
        foreach (IGateStep step in GateProfiles.For(run.Profile).InOrder)
        {
            GateStepResult result = await step.RunAsync(run, facts, cancellationToken);
            facts = result.Facts;
            if (!await PassesAsync(result.Step, steps, progress))
            {
                break;
            }
        }

        GateResult gateResult = Result(run, steps, facts.TestsTotal);
        if (VerdictNames.AllowsNextStep(gateResult.Verdict) && facts.Selection?.PassRecord is StopPassRecord passed)
        {
            // The next stop run of this target chooses its projects by what changed since this PASS.
            await passed.RememberAsync(cancellationToken);
        }

        return gateResult;
    }

    private static async Task<bool> PassesAsync(GateStep step, List<GateStep> steps, TextWriter progress)
    {
        steps.Add(step);
        await progress.WriteLineAsync($"{step.Name}: {VerdictNames.ToContractName(step.Verdict)} ({step.DurationMs} ms)");
        if (step.Detail.Length > 0)
        {
            await progress.WriteLineAsync($"  {step.Detail}");
        }

        return VerdictNames.AllowsNextStep(step.Verdict);
    }

    /// <summary>The run's verdict is that of its last step that applied: steps run only while every earlier one passed or did not apply.</summary>
    private static GateResult Result(GateRunContext run, List<GateStep> steps, int? testsTotal) =>
        new(run.Target, run.Profile, steps.LastOrDefault(step => step.Verdict != Verdict.NotApplicable)?.Verdict ?? Verdict.NotApplicable, testsTotal, steps);
}
