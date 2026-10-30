using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Affected;
using Lockflight.Cli.Gates.Steps;

namespace Lockflight.Cli.Gates;

/// <summary>
/// Runs a profile over a workspace's units. Checks that read the whole workspace's changes run once at the root —
/// pr: <c>policy.protected</c>; every profile: format; pr: secrets last — and each .NET unit runs build, test and, in pr, patch
/// coverage and dependencies in its own directory. The stop profile judges a unit whose state differs from its last PASS:
/// uncommitted changes, a commit, a change to shared build inputs outside every unit (<see cref="SharedChanges"/>, committed or
/// not), or no PASS yet. The Stop hook also skips a unit with uncommitted changes whose state matches its cached PASS; a
/// <c>gate run</c> judges every unit with uncommitted changes. The edit profile runs format only. A stop-profile PASS of a unit
/// is remembered for both. Separate repositories run in parallel, at most <see cref="MaxParallelUnits"/> at a time; units of one
/// repository run one at a time (shared projects, shared obj/). All share the run's budget. A unit that cannot run is ERROR.
/// The run's verdict is the worst verdict of any step.
/// </summary>
internal static class WorkspaceGateRunner
{
    public const int MaxParallelUnits = 2;

    public static async Task<GateResult> RunAsync(
        Manifest manifest, GateRunContext root, IReadOnlyCollection<string> onlyUnits, bool useCache, TextWriter progress, CancellationToken cancellationToken)
    {
        GateProfileDefinition definition = GateProfiles.For(root.Profile);
        List<GateStep> steps = [];
        Progress report = new(progress);
        IReadOnlyList<(Unit Unit, UnitState State)> selected = await ReadStatesAsync(manifest, onlyUnits, cancellationToken);

        bool judgesChangesOnly = root.Profile != GateProfile.PullRequest;
        List<(Unit Unit, UnitState State)> toRun = [];
        foreach ((Unit unit, UnitState state) in selected)
        {
            if (!Directory.Exists(Path.Combine(manifest.Root, unit.Path.Replace('/', Path.DirectorySeparatorChar))))
            {
                // A unit that is not cloned here (another repository, or a CI checkout of the shell alone) has nothing to build.
                await report.AddAsync(steps, new GateStep(unit.Path, 0, Verdict.NotApplicable, 0, "not present in this checkout: not built"));
            }
            else if (judgesChangesOnly && !state.Dirty && (state.Fingerprint is null || !BuildsWithDotnet(manifest, unit)))
            {
                await report.AddAsync(steps, new GateStep(unit.Path, 0, Verdict.NotApplicable, 0, "no changes, not run"));
            }
            else if (!BuildsWithDotnet(manifest, unit))
            {
                await report.AddAsync(steps, new GateStep(unit.Path, 0, Verdict.NotApplicable, 0, "not a .NET unit: guarded, not built"));
            }
            else if (judgesChangesOnly && (useCache || !state.Dirty) && state.Fingerprint is string fingerprint
                && await new StopGateCache(manifest.Root, unit.Path).PassedWithAsync(fingerprint, cancellationToken))
            {
                await report.AddAsync(steps, new GateStep(unit.Path, 0, Verdict.NotApplicable, 0, "unchanged since its last PASS"));
            }
            else if (definition.PerUnit.Count > 0)
            {
                // Uncommitted changes, a commit since the last PASS, a shared build input, or never judged: the unit is judged.
                toRun.Add((unit, state));
            }
        }

        // Format judges uncommitted changed lines, so it runs only when a unit or the shell has some.
        bool anyChange = !judgesChangesOnly || selected.Any(entry => entry.State.Dirty);
        foreach (IGateStep step in definition.RootFirst.Where(step => anyChange || step.Name != FormatStep.StepName))
        {
            if (!await report.AddAsync(steps, (await step.RunAsync(root, GateRunFacts.None, cancellationToken)).Step))
            {
                return Result(root, steps, testsTotal: null);
            }
        }

        // A profile without unit steps (edit) ends at the root.
        if (definition.PerUnit.Count == 0)
        {
            return Result(root, steps, testsTotal: null);
        }

        UnitOutcome[] outcomes = await RunUnitsAsync(manifest, root, definition.PerUnit, toRun, cancellationToken);
        foreach (UnitOutcome outcome in outcomes)
        {
            foreach (GateStep step in outcome.Steps)
            {
                await report.AddAsync(steps, step);
            }

            if (root.Profile == GateProfile.Stop && outcome.Steps.All(step => VerdictNames.AllowsNextStep(step.Verdict)) && outcome.State.Fingerprint is string fingerprint)
            {
                await new StopGateCache(manifest.Root, outcome.Unit.Path).RememberPassAsync(fingerprint, cancellationToken);
            }

            if (outcome.Steps.All(step => VerdictNames.AllowsNextStep(step.Verdict)) && outcome.Selection?.PassRecord is StopPassRecord passed)
            {
                await passed.RememberAsync(cancellationToken);
            }
        }

        int? testsTotal = outcomes.Any(outcome => outcome.TestsTotal is not null) ? outcomes.Sum(outcome => outcome.TestsTotal ?? 0) : null;
        // The steps after the units (pr: secrets) run only when every earlier step passed or did not apply.
        if (steps.All(step => VerdictNames.AllowsNextStep(step.Verdict)))
        {
            foreach (IGateStep step in definition.RootLast)
            {
                if (!await report.AddAsync(steps, (await step.RunAsync(root, GateRunFacts.None, cancellationToken)).Step))
                {
                    break;
                }
            }
        }

        return Result(root, steps, testsTotal);
    }

    /// <summary>
    /// Each selected unit's state, keyed with the engine, the settings and the shared build inputs outside every unit (a referenced
    /// libs/ folder, a root Directory.Build.props), committed and uncommitted. Uncommitted shared changes make every unit changed.
    /// </summary>
    public static async Task<IReadOnlyList<(Unit Unit, UnitState State)>> ReadStatesAsync(
        Manifest manifest, IReadOnlyCollection<string> onlyUnits, CancellationToken cancellationToken)
    {
        string? shared = await SharedChanges.FingerprintAsync(manifest, cancellationToken);
        string committedShared = await SharedChanges.CommittedFingerprintAsync(manifest, cancellationToken);
        string key = $"{InputsHash.EngineAndSettings(manifest.Root)}|shared:{committedShared}|{shared}";
        List<(Unit Unit, UnitState State)> states = [];
        foreach (Unit unit in manifest.Units.Where(unit => onlyUnits.Count == 0 || onlyUnits.Contains(unit.Path, StringComparer.OrdinalIgnoreCase)))
        {
            UnitState state = await UnitState.ReadAsync(manifest, unit, $"{key}|{InputsHash.UnitStepsKey(unit)}", cancellationToken);
            states.Add((unit, shared is null ? state : state with { Dirty = true }));
        }

        return states;
    }

    /// <summary>Whether the gates build a unit: <c>kind: dotnet</c>, or <c>auto</c> with a solution or project file in its directory.</summary>
    public static bool BuildsWithDotnet(Manifest manifest, Unit unit)
    {
        string directory = Path.Combine(manifest.Root, unit.Path.Replace('/', Path.DirectorySeparatorChar));
        return unit.Kind switch
        {
            UnitKind.Dotnet => true,
            UnitKind.None => false,
            _ => Directory.Exists(directory)
                && (unit.Build is not null || Directory.EnumerateFiles(directory).Any(file => file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))),
        };
    }

    private static async Task<UnitOutcome[]> RunUnitsAsync(
        Manifest manifest, GateRunContext root, IReadOnlyList<IGateStep> unitSteps, List<(Unit Unit, UnitState State)> units, CancellationToken cancellationToken)
    {
        // Units of one repository may share projects and obj/ folders, so they build one at a time; separate repositories in parallel.
        bool allSeparate = units.All(entry => Adoption.UnitDiscovery.IsRepository(Path.Combine(manifest.Root, entry.Unit.Path.Replace('/', Path.DirectorySeparatorChar))));
        using SemaphoreSlim slots = new(allSeparate ? MaxParallelUnits : 1);
        return await Task.WhenAll(units.Select(async entry =>
        {
            await slots.WaitAsync(cancellationToken);
            try
            {
                return await RunUnitAsync(manifest, root, unitSteps, entry.Unit, entry.State, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One unit's failure to run is that unit's ERROR, never a pass for the workspace.
                return new UnitOutcome(entry.Unit, entry.State with { Fingerprint = null }, [new GateStep($"{entry.Unit.Path}: run", 0, Verdict.Error, 0, $"the unit's gates could not run: {exception.Message}")], TestsTotal: null);
            }
            finally
            {
                slots.Release();
            }
        }));
    }

    /// <summary>The profile's unit steps (build, test and — in pr — coverage and dependencies) in one unit, stopping at its first step that does not pass.</summary>
    private static async Task<UnitOutcome> RunUnitAsync(
        Manifest manifest, GateRunContext root, IReadOnlyList<IGateStep> unitSteps, Unit unit, UnitState state, CancellationToken cancellationToken)
    {
        // The unit shares the run's deadline: its own context would start a fresh budget.
        string directory = Path.Combine(manifest.Root, unit.Path.Replace('/', Path.DirectorySeparatorChar));
        GateRunContext run = (root with { Target = directory, UnitDirectories = GateRunContext.UnitDirectoriesOf(manifest) }).ForUnit(unit, directory);

        List<GateStep> steps = [];
        GateRunFacts facts = GateRunFacts.None;
        foreach (IGateStep step in unitSteps)
        {
            GateStepResult result = await step.RunAsync(run, facts, cancellationToken);
            facts = result.Facts;
            steps.Add(Named(unit, result.Step));
            if (!VerdictNames.AllowsNextStep(result.Step.Verdict))
            {
                break;
            }
        }

        return new UnitOutcome(unit, state, steps, facts.TestsTotal) { Selection = facts.Selection };
    }

    private static GateStep Named(Unit unit, GateStep step) => step with { Name = $"{unit.Path}: {step.Name}", Unit = unit.Path };

    /// <summary>The worst verdict of the steps that applied: ERROR, then FAIL, then NOT-EARNED, then PASS; N/A when none applied.</summary>
    private static GateResult Result(GateRunContext root, List<GateStep> steps, int? testsTotal)
    {
        Verdict[] applied = [.. steps.Select(step => step.Verdict).Where(verdict => verdict != Verdict.NotApplicable)];
        Verdict verdict = applied.Length == 0 ? Verdict.NotApplicable
            : applied.Contains(Verdict.Error) ? Verdict.Error
            : applied.Contains(Verdict.Fail) ? Verdict.Fail
            : applied.Contains(Verdict.NotEarned) ? Verdict.NotEarned
            : Verdict.Pass;
        return new GateResult(root.Target, root.Profile, verdict, testsTotal, steps);
    }

    /// <summary>One unit's steps, its state when the run began (the cache key a PASS is remembered under) and its test count.</summary>
    private sealed record UnitOutcome(Unit Unit, UnitState State, IReadOnlyList<GateStep> Steps, int? TestsTotal)
    {
        /// <summary>The unit's choice of projects (stop profile), whose record a PASS keeps for the next run.</summary>
        public ProjectSelection? Selection { get; init; }
    }

    /// <summary>Collects steps and prints each as it is added, in the format of <see cref="GateRunner"/>.</summary>
    private sealed class Progress(TextWriter writer)
    {
        public async Task<bool> AddAsync(List<GateStep> steps, GateStep step)
        {
            steps.Add(step);
            await writer.WriteLineAsync($"{step.Name}: {VerdictNames.ToContractName(step.Verdict)} ({step.DurationMs} ms)");
            if (step.Detail.Length > 0)
            {
                await writer.WriteLineAsync($"  {step.Detail}");
            }

            return VerdictNames.AllowsNextStep(step.Verdict);
        }
    }
}
