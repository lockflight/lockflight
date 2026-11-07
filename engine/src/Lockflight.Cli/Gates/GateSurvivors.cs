using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes.Survivors;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Gates;

/// <summary>
/// The <c>pr</c> gates in CI (<c>gate run --profile pr</c> with <c>CI</c> set, as the CI files that run the gates alone do) sweep for
/// processes the pull request's build and tests leave running, as the CI judge does: whenever the engine has no child running, each
/// one found is stopped and listed. One that could not be stopped adds a <c>processes</c> row that is ERROR
/// (<see cref="ReasonCode.SurvivorNotStopped"/>), so the run is ERROR. Docker containers are not looked at: a CI agent's Docker
/// daemon may be shared with other jobs.
/// </summary>
internal static class GateSurvivors
{
    /// <summary>The id of the row a process that could not be stopped adds.</summary>
    public const string StepId = "processes";

    /// <summary>The variable CI services (GitHub Actions, GitLab CI/CD, Azure Pipelines, Bitbucket Pipelines) set in every job.</summary>
    public const string CiVariable = "CI";

    private const int MaxCommandShown = 200;

    /// <summary>Whether this run sweeps: a pr run in CI, and not one the CI judge runs inside its own sweep (<paramref name="current"/>).</summary>
    public static bool Sweeps(GateProfile profile, EnvironmentReader environment, ProcessSweep? current) =>
        profile == GateProfile.PullRequest && !string.IsNullOrEmpty(environment.Get(CiVariable)) && current is null;

    /// <summary>The result with a <see cref="StepId"/> row that is ERROR when a process or container could not be stopped; else as it was.</summary>
    public static GateResult Apply(GateResult result, ProcessesReport processes) =>
        processes.NotStopped == 0
            ? result
            : result with { Verdict = Verdict.Error, Steps = [.. result.Steps, Step(processes)] };

    /// <summary>The lines that say what was left running and what became of it, or why nothing was checked.</summary>
    public static IEnumerable<string> Lines(ProcessesReport processes)
    {
        if (processes.Mechanism == SweepMechanism.None)
        {
            yield return $"Processes left running were not checked: {processes.Note}";
            yield break;
        }

        foreach (Survivor survivor in processes.Survivors.Where(survivor => survivor.Process.Outcome != SurvivorOutcome.Exited))
        {
            string what = survivor.Process.Kind == SurvivorKind.Container ? $"container, image '{Shown(survivor.Process.Image)}'" : $"pid {survivor.Process.Pid}";
            yield return $"Left running ({SurvivorOutcomeNames.ToName(survivor.Process.Outcome)}): '{Shown(survivor.Process.Command)}' ({what}, after '{Shown(survivor.After)}')";
        }

        if (processes.More > 0)
        {
            yield return $"Left running: {processes.More} more";
        }
    }

    private static GateStep Step(ProcessesReport processes) =>
        new(StepId, 0, Verdict.Error, ReasonCode.SurvivorNotStopped, 0,
            $"{processes.NotStopped} {(processes.NotStopped == 1 ? "process" : "processes")} the pull request's build or tests started could not be stopped "
            + "(another user's, typically started through sudo), so nothing the gates read after it started can be trusted");

    private static string Shown(string text) => TerminalText.Clean(text.Length <= MaxCommandShown ? text : $"{text[..MaxCommandShown]}…");
}
