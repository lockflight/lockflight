using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;

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

    /// <summary>The unit's <c>test.stopFilter</c>, else the root's <c>gates.test.stopFilter</c>: tests the stop profile leaves to the pr profile; null for none.</summary>
    public string? StopFilter { get; init; }

    /// <summary>
    /// <c>gate run --full</c>: the stop profile builds and tests every project instead of the ones the uncommitted changes affect.
    /// </summary>
    public bool FullRun { get; init; }

    /// <summary>The full paths of the workspace's units; a change inside another unit does not choose this one's projects.</summary>
    public IReadOnlyList<string> UnitDirectories { get; init; } = [];

    /// <summary>Runs the tools the steps judge (the .NET SDK, gitleaks); real processes unless a test supplies its own.</summary>
    public RunTimedProcess Processes { get; init; } = ProcessRunner.RunAsync;

    /// <summary>
    /// The run for one directory of a project: in a unit of a workspace, that unit's <c>build:</c> file (resolved from the target),
    /// test framework and <c>test.stopFilter</c>; outside every unit, the root's <c>gates.build</c>. Every command that runs a unit's
    /// steps outside a workspace run (<c>gate run --path</c>, <c>baseline</c>) starts here, so it builds what the workspace run builds.
    /// </summary>
    public static GateRunContext ForTarget(
        Manifest manifest, string target, GateProfile profile, string? baseRef, TimeProvider time, bool underStopHook = true)
    {
        GateRunContext run = new(target, manifest.Root, profile, manifest.Gates, baseRef, time, underStopHook) { UnitDirectories = UnitDirectoriesOf(manifest) };
        return UnitResolver.Owning(manifest, target) is Unit unit
            ? run.ForUnit(unit, Path.Combine(manifest.Root, unit.Path.Replace('/', Path.DirectorySeparatorChar)))
            : run with { BuildFile = manifest.Gates.BuildFile, StopFilter = manifest.Gates.StopFilter };
    }

    /// <summary>
    /// This run with a unit's settings: its <c>build:</c> file (relative to the unit's directory, passed relative to the target),
    /// its test framework and stop filter (else the root's). The root's <c>gates.build</c> never applies to a unit.
    /// </summary>
    public GateRunContext ForUnit(Unit unit, string unitDirectory) => this with
    {
        Settings = Settings with { TestFramework = unit.TestFramework ?? Settings.TestFramework },
        UnitPath = unit.Path,
        BuildFile = unit.Build is null ? null : Path.GetRelativePath(Target, Path.Combine(unitDirectory, unit.Build.Replace('/', Path.DirectorySeparatorChar))),
        StopFilter = unit.StopFilter ?? Settings.StopFilter,
    };

    /// <summary>The full paths of a manifest's units.</summary>
    public static IReadOnlyList<string> UnitDirectoriesOf(Manifest manifest) =>
        [.. manifest.Units.Select(unit => Path.Combine(manifest.Root, unit.Path.Replace('/', Path.DirectorySeparatorChar)))];

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
