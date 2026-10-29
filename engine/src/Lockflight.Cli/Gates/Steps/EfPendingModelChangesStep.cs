using System.Diagnostics;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Ef;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>
/// An EF Core model changed without a migration (pr profile, per unit, after the build): for every DbContext with migrations
/// (<see cref="EfContexts"/>) whose project, or a project it references, the change touches (since the merge base with
/// <c>--base</c>, plus uncommitted and untracked files), <c>dotnet ef migrations has-pending-model-changes</c> (EF Core 8 or later)
/// must report none. It runs only when the repository pins <c>dotnet-ef</c> in a tool manifest (<see cref="EfToolPin"/>, restored
/// first) under <c>gates.ef.pendingModelChanges: auto</c>, the default; <c>fail</c> runs it with whatever <c>dotnet ef</c> the
/// machine has, <c>report</c> lists without failing, <c>off</c> skips it. Every run shares the gate's time budget.
/// </summary>
internal sealed class EfPendingModelChangesStep : IGateStep
{
    public const string StepName = "ef.pendingModelChanges";

    public const string Setting = "gates.ef.pendingModelChanges";

    /// <summary>What <c>dotnet ef migrations has-pending-model-changes</c> prints when the model has changes without a migration.</summary>
    private const string PendingMessage = "Changes have been made to the model since the last migration";

    private const int MaxLineLength = 300;

    public string Name => StepName;

    public StepScope Scope => StepScope.Unit;

    public string CacheKey => StepName;

    public async Task<GateStepResult> RunAsync(GateRunContext run, GateRunFacts facts, CancellationToken cancellationToken)
    {
        GateMode mode = run.Settings.EfPendingModelChanges;
        GateStep step = mode == GateMode.Off ? StepModes.Off(StepName, Setting) : StepModes.Apply(await CheckAsync(run, mode, cancellationToken), mode, Setting);
        return new GateStepResult(step, facts);
    }

    private static async Task<GateStep> CheckAsync(GateRunContext run, GateMode mode, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        IReadOnlyList<EfContext> contexts = EfContexts.Find(run.ProjectRoot, run.Target);
        if (contexts.Count == 0)
        {
            return NotApplicable(stopwatch, "no EF Core migrations (Migrations/*ModelSnapshot.cs) here");
        }

        string? pin = EfToolPin.Find(run.ProjectRoot, run.Target);
        if (pin is null && mode == GateMode.Auto)
        {
            return NotApplicable(stopwatch, $"{EfToolPin.ToolName} is not pinned in a tool manifest (dotnet-tools.json), so not run; pin it, or set {Setting}: fail to run the dotnet ef this machine has");
        }

        if (await ChangedPathsAsync(run, cancellationToken) is not IReadOnlySet<string> changed)
        {
            return new GateStep(StepName, 0, Verdict.Error, stopwatch.ElapsedMilliseconds,
                $"git could not list the changed files{(run.BaseRef is null ? string.Empty : $" since '{run.BaseRef}'; is the checkout deep enough (fetch-depth: 0)?")}");
        }

        EfContext[] touched = [.. contexts.Where(context => changed.Any(path => context.ModelDirectories.Any(directory => Under(path, directory))))];
        if (touched.Length == 0)
        {
            return NotApplicable(stopwatch, $"no change touches an EF Core project ({string.Join(", ", contexts.Select(context => context.Describe()))})");
        }

        string restoreOutput = string.Empty;
        if (pin is not null)
        {
            ProcessResult restore = await run.Processes("dotnet", ["tool", "restore"], run.Target, run.TimeLeft(), cancellationToken);
            if (StepHealth.Unhealthy(StepName, restore, run) is GateStep unhealthy)
            {
                return unhealthy with { DurationMs = stopwatch.ElapsedMilliseconds };
            }

            // A manifest may pin a tool that does not restore here (the engine's own pin on an older SDK); dotnet ef still may.
            restoreOutput = restore.ExitCode == 0 ? string.Empty : $" (dotnet tool restore exited {restore.ExitCode}: {LastLine(restore.Combined)})";
        }

        List<string> pending = [];
        foreach (EfContext context in touched)
        {
            ProcessResult check = await run.Processes("dotnet", Arguments(run, context), run.Target, run.TimeLeft(), cancellationToken);
            if (StepHealth.Unhealthy(StepName, check, run) is GateStep unhealthy)
            {
                return unhealthy with { DurationMs = stopwatch.ElapsedMilliseconds };
            }

            if (check.Combined.Contains(PendingMessage, StringComparison.Ordinal))
            {
                pending.Add(context.Describe());
            }
            else if (check.ExitCode != 0)
            {
                return new GateStep(StepName, check.ExitCode, Verdict.Error, stopwatch.ElapsedMilliseconds,
                    $"dotnet ef could not check {context.Describe()}: {LastLine(check.Combined)}{restoreOutput} (the check needs EF Core 8 or later and a startup project referencing Microsoft.EntityFrameworkCore.Design)");
            }
        }

        string checkedContexts = $"{touched.Length} DbContexts checked: {string.Join(", ", touched.Select(context => context.Describe()))}";
        return pending.Count == 0
            ? new GateStep(StepName, 0, Verdict.Pass, stopwatch.ElapsedMilliseconds, checkedContexts)
            : new GateStep(StepName, 1, Verdict.Fail, stopwatch.ElapsedMilliseconds,
                $"{pending.Count} models changed without a migration: {string.Join(", ", pending)}; add one (dotnet ef migrations add <name>) and commit it with the change");
    }

    private static string[] Arguments(GateRunContext run, EfContext context)
    {
        List<string> arguments = ["ef", "migrations", "has-pending-model-changes", "--project", Relative(run, context.Project), "--context", context.Context];
        if (context.StartupProject is string startup)
        {
            arguments.AddRange(["--startup-project", Relative(run, startup)]);
        }

        return [.. arguments];
    }

    /// <summary>A project path (relative to the project root) as seen from the unit's directory, where dotnet runs.</summary>
    private static string Relative(GateRunContext run, string path) =>
        Path.GetRelativePath(run.Target, Path.Combine(run.ProjectRoot, path.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The paths changed since the comparison point, relative to the project root; null when git fails.</summary>
    private static async Task<IReadOnlySet<string>?> ChangedPathsAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        if (await Git.UncommittedBaseAsync(run.ProjectRoot, cancellationToken) is not string uncommittedBase)
        {
            return null;
        }

        List<string> ranges = [uncommittedBase];
        if (run.BaseRef is not null)
        {
            ranges.Add(Git.SinceMergeBase(run.BaseRef));
        }

        HashSet<string> changed = new(StringComparer.Ordinal);
        foreach (string range in ranges)
        {
            if (await WorkspaceGit.ChangedNamesAsync(run.ProjectRoot, range, run.TimeLeft(), cancellationToken) is not IReadOnlyList<string> names)
            {
                return null;
            }

            changed.UnionWith(names);
        }

        if (await WorkspaceGit.UntrackedFilesAsync(run.ProjectRoot, [], run.TimeLeft(), cancellationToken) is not IReadOnlyList<string> untracked)
        {
            return null;
        }

        changed.UnionWith(untracked);
        return changed;
    }

    private static bool Under(string path, string directory) =>
        directory is "." or "" || path.StartsWith(directory + "/", StringComparison.Ordinal);

    private static GateStep NotApplicable(Stopwatch stopwatch, string detail) =>
        new(StepName, 0, Verdict.NotApplicable, stopwatch.ElapsedMilliseconds, detail);

    private static string LastLine(string output)
    {
        string line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "no output";
        return line.Length > MaxLineLength ? line[..MaxLineLength] + "…" : line;
    }
}
