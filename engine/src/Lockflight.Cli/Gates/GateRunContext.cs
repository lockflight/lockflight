using Lockflight.Cli.Config;

namespace Lockflight.Cli.Gates;

/// <summary>
/// Everything a gate step needs: where to run, the project's settings, the comparison base (CI) and the clock. The run's time budget
/// starts when the context is created and is shared by all steps: <c>gates.timeoutSeconds</c>, capped for the stop profile at
/// <see cref="StopHookBudgetSeconds"/> so the run can still report before Claude Code kills the Stop hook. A person's run of the stop
/// steps outside the hook (<c>baseline tests</c>) passes <paramref name="UnderStopHook"/> false and keeps the full budget.
/// </summary>
internal sealed record GateRunContext(
    string Target, string ProjectRoot, GateProfile Profile, GateSettings Settings, string? BaseRef, TimeProvider Time, bool UnderStopHook = true)
{
    /// <summary>
    /// The unit of a workspace the target lies in, whose baselines the steps read; null for a single repository or a
    /// target outside every unit.
    /// </summary>
    public string? UnitPath { get; init; }

    /// <summary>The unit's <c>build:</c> solution or project file, passed to build, test and package listing; null for the one in the target.</summary>
    public string? BuildFile { get; init; }

    /// <summary>The project file argument for <c>dotnet build</c> and <c>dotnet test</c>: the build file, if any.</summary>
    public IReadOnlyList<string> BuildFileArguments => BuildFile is null ? [] : [BuildFile];

    /// <summary>The unit's <c>test.stopFilter</c>: tests the stop profile leaves to the pr profile; null for none.</summary>
    public string? StopFilter { get; init; }

    /// <summary>The plugin's Stop hook timeout (<c>plugins/lockflight/hooks/hooks.json</c>), in seconds.</summary>
    public const int StopHookTimeoutSeconds = 600;

    /// <summary>The stop profile's budget ceiling: below the hook timeout, leaving time to report and to record the result.</summary>
    public const int StopHookBudgetSeconds = 540;

    /// <summary>The run's budget in seconds.</summary>
    public int BudgetSeconds { get; } = BudgetFor(Profile, Settings, UnderStopHook);

    public DateTimeOffset Deadline { get; } = Time.GetUtcNow().AddSeconds(BudgetFor(Profile, Settings, UnderStopHook));

    /// <summary>Where the budget comes from, for a step that ran out of it.</summary>
    public string BudgetSource => BudgetSeconds < Settings.TimeoutSeconds
        ? $"the stop profile's {StopHookBudgetSeconds} s cap, below the plugin's {StopHookTimeoutSeconds} s Stop hook timeout (gates.timeoutSeconds is {Settings.TimeoutSeconds}); " +
          "a run this long belongs in CI: gate run --profile pr"
        : "gates.timeoutSeconds";

    /// <summary>Time left for the next tool run; never zero, so a late step still reports instead of hanging.</summary>
    public TimeSpan TimeLeft()
    {
        TimeSpan remaining = Deadline - Time.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
    }

    private static int BudgetFor(GateProfile profile, GateSettings settings, bool underStopHook) =>
        profile == GateProfile.Stop && underStopHook ? Math.Min(settings.TimeoutSeconds, StopHookBudgetSeconds) : settings.TimeoutSeconds;
}
