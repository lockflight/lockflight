using Lockflight.Cli.Gates.Steps;

namespace Lockflight.Cli.Gates;

/// <summary>
/// Runs the steps of a profile in order — edit: format · stop: format, build, test · pr: a protected-changes report, then the stop
/// steps, patch coverage, dependencies and secrets — and stops at the first step that does not pass, so the agent gets the most
/// actionable failure first.
/// </summary>
internal static class GateRunner
{
    public static async Task<GateResult> RunAsync(GateRunContext run, TextWriter progress, CancellationToken cancellationToken)
    {
        List<GateStep> steps = [];

        // Reported first so that policy changes are visible even when a later step fails.
        if (run.Profile == GateProfile.PullRequest && !await PassesAsync(await ProtectedChangesStep.RunAsync(run, cancellationToken), steps, progress))
        {
            return Result(run, steps, testsTotal: null);
        }

        if (!await PassesAsync(await FormatStep.RunAsync(run, cancellationToken), steps, progress) || run.Profile == GateProfile.Edit)
        {
            return Result(run, steps, testsTotal: null);
        }

        if (!await PassesAsync(await BuildStep.RunAsync(run, cancellationToken), steps, progress))
        {
            return Result(run, steps, testsTotal: null);
        }

        DateTimeOffset testsStarted = run.Time.GetUtcNow();
        TestStepOutcome tests = await TestStep.RunAsync(run, cancellationToken);
        if (!await PassesAsync(tests.Step, steps, progress) || run.Profile != GateProfile.PullRequest)
        {
            return Result(run, steps, tests.TestsTotal);
        }

        // CI-only checks: they need the network or coverage instrumentation, too slow for the end of every agent turn.
        if (!await PassesAsync(await CoverageStep.RunAsync(run, testsStarted, tests.ResultsDirectory, cancellationToken), steps, progress))
        {
            return Result(run, steps, tests.TestsTotal);
        }

        (GateStep dependencies, _) = await DependencyStep.RunAsync(run, cancellationToken);
        if (!await PassesAsync(dependencies, steps, progress))
        {
            return Result(run, steps, tests.TestsTotal);
        }

        await PassesAsync(await SecretsStep.RunAsync(run, cancellationToken), steps, progress);
        return Result(run, steps, tests.TestsTotal);
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
