using Lockflight.Cli.Gates.Affected;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>
/// The stop profile's choice of projects (<see cref="ProjectSelector"/>), first among its unit steps: the build and test steps
/// after it run the projects it names. It judges nothing, so it is N/A; its detail says which projects run and why, in the
/// result and the ledger.
/// </summary>
internal sealed class AffectedProjectsStep : IGateStep
{
    public const string StepName = "projects.affected";

    public string Name => StepName;

    public StepScope Scope => StepScope.Unit;

    public string CacheKey => $"{StepName}/{ProjectSelector.RulesVersion}";

    public async Task<GateStepResult> RunAsync(GateRunContext run, GateRunFacts facts, CancellationToken cancellationToken)
    {
        long started = run.Time.GetTimestamp();
        ProjectSelection selection = await ProjectSelector.SelectAsync(run, cancellationToken);
        long durationMs = (long)run.Time.GetElapsedTime(started).TotalMilliseconds;
        return new(new GateStep(StepName, 0, Verdict.NotApplicable, durationMs, selection.Describe(), selection.Scope), facts with { Selection = selection });
    }
}
