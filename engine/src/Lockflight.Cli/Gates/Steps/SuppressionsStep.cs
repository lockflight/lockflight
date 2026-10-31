using System.Diagnostics;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Gates.Suppressions;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>
/// New suppressions (pr profile): a <c>#pragma warning disable</c>, <c>[SuppressMessage]</c> (also in <c>GlobalSuppressions.cs</c>),
/// <c>&lt;NoWarn&gt;</c>, <c>&lt;WarningsNotAsErrors&gt;</c>, a severity lowered in <c>.editorconfig</c> or a <c>.globalconfig</c>,
/// <c>#nullable disable</c> or <c>[ExcludeFromCodeCoverage]</c> on a line the change adds (<see cref="SuppressionScan"/>). Each one
/// silences a check the other gates rely on, so it fails unless a person accepted that rule in that file
/// (<see cref="AcceptedSuppressions"/>). <c>gates.quality.suppressions</c> decides whether the step can fail, only reports, or is off.
/// </summary>
internal sealed class SuppressionsStep : IGateStep
{
    public const string StepName = "quality.suppressions";

    public const string Setting = "gates.quality.suppressions";

    private const int MaxListed = 10;

    public string Name => StepName;

    public StepScope Scope => StepScope.Root;

    public string CacheKey => StepName;

    public GateMode ModeUnder(GateSettings settings) => settings.Suppressions;

    public async Task<GateStepResult> RunAsync(GateRunContext run, GateRunFacts facts, CancellationToken cancellationToken)
    {
        GateMode mode = run.Settings.Suppressions;
        GateStep step = mode == GateMode.Off ? StepModes.Off(StepName, Setting) : StepModes.Apply(await CheckAsync(run, cancellationToken), mode, Setting);
        return new GateStepResult(step, facts);
    }

    private static async Task<GateStep> CheckAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        SuppressionScanResult scan = await SuppressionScan.FindAsync(run.ProjectRoot, run.BaseRef, run.TimeLeft(), cancellationToken);
        if (scan.Found is not IReadOnlyList<Suppression> found)
        {
            return new GateStep(StepName, 0, Verdict.Error, stopwatch.ElapsedMilliseconds, scan.Problem ?? "the suppressions check could not run");
        }

        string scope = run.BaseRef is null ? "uncommitted changes checked (pass --base to check the branch's commits)" : string.Empty;
        if (scan.FilesChecked == 0)
        {
            return new GateStep(StepName, 0, Verdict.NotApplicable, stopwatch.ElapsedMilliseconds, "no changed C#, MSBuild or analyzer config files");
        }

        IReadOnlySet<string> accepted = new AcceptedSuppressions(run.ProjectRoot).Load();
        Suppression[] fresh = [.. found.Where(suppression => !accepted.Contains(suppression.Key))];
        int acceptedCount = found.Count - fresh.Length;
        if (fresh.Length == 0)
        {
            string acceptedNote = acceptedCount > 0 ? $"{acceptedCount} added suppressions accepted by {AcceptedSuppressions.RelativePath}" : string.Empty;
            string[] parts = [$"{scan.FilesChecked} changed files checked", acceptedNote, scope];
            return new GateStep(StepName, 0, Verdict.Pass, stopwatch.ElapsedMilliseconds, string.Join("; ", parts.Where(part => part.Length > 0)));
        }

        string listed = string.Join("; ", fresh.Take(MaxListed).Select(suppression => suppression.Describe()));
        string more = fresh.Length > MaxListed ? $"; and {fresh.Length - MaxListed} more" : string.Empty;
        return new GateStep(StepName, 0, Verdict.Fail, stopwatch.ElapsedMilliseconds,
            $"{fresh.Length} new suppressions: {listed}{more}; fix what they silence, or a person accepts a needed one (file and rule) with {AcceptCommand(run.ProjectRoot)}");
    }

    private static string AcceptCommand(string root) =>
        HumanOnlyCommands.InSessionFor("baseline suppressions", root) ?? $"`{EngineCommand.Linked} baseline suppressions` in a separate terminal window";
}
