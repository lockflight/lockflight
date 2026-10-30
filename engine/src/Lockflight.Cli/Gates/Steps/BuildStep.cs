using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>Build; fails on errors and on warnings beyond the accepted baseline (brownfield: "no new problems").</summary>
internal sealed class BuildStep : IGateStep
{
    public const string StepName = "build";

    private const int MaxListed = 3;

    public string Name => StepName;

    public StepScope Scope => StepScope.Unit;

    public string CacheKey => StepName;

    /// <summary>The build of the projects the stop profile chose (<see cref="GateRunFacts.Selection"/>), else of everything.</summary>
    public async Task<GateStepResult> RunAsync(GateRunContext run, GateRunFacts facts, CancellationToken cancellationToken) =>
        new(await BuildAsync(run, facts.Selection?.FilterFile is string filter ? [filter] : run.BuildFileArguments, cancellationToken), facts);

    /// <summary>The build's verdict over everything; <see cref="Baselines.BaselineRun"/> calls it directly before it takes a test baseline.</summary>
    public static Task<GateStep> BuildAsync(GateRunContext run, CancellationToken cancellationToken) =>
        BuildAsync(run, run.BuildFileArguments, cancellationToken);

    private static async Task<GateStep> BuildAsync(GateRunContext run, IReadOnlyList<string> projectArguments, CancellationToken cancellationToken)
    {
        ProcessResult buildRun = await run.Processes("dotnet", ["build", .. projectArguments, "--nologo", "--interactive", "false"], run.Target, run.TimeLeft(), cancellationToken);
        if (StepHealth.Unhealthy(StepName, buildRun, run) is GateStep unhealthy)
        {
            return unhealthy;
        }

        if (buildRun.ExitCode != 0)
        {
            return new GateStep(StepName, buildRun.ExitCode, Verdict.Fail, buildRun.DurationMs, FirstErrorLine(buildRun.Combined));
        }

        IReadOnlyList<BuildDiagnostic> diagnostics = BuildDiagnostics.Parse(buildRun.Output, run.ProjectRoot);
        DiagnosticsBaseline baseline = new(run.ProjectRoot, run.UnitPath);
        if (!baseline.Exists)
        {
            string note = diagnostics.Count == 0 ? string.Empty : $"{diagnostics.Count} warnings, no baseline yet (a person types /lockflight:baseline build in Claude Code, or opens a separate terminal window and runs `.lockflight/bin/lockflight baseline build`)";
            return new GateStep(StepName, buildRun.ExitCode, Verdict.Pass, buildRun.DurationMs, note);
        }

        IReadOnlyList<BuildDiagnostic> fresh = baseline.NewIn(diagnostics);
        return fresh.Count == 0
            ? new GateStep(StepName, buildRun.ExitCode, Verdict.Pass, buildRun.DurationMs, string.Empty)
            : new GateStep(StepName, buildRun.ExitCode, Verdict.Fail, buildRun.DurationMs, DescribeNew(fresh));
    }

    private static string DescribeNew(IReadOnlyList<BuildDiagnostic> fresh)
    {
        string listed = string.Join("; ", fresh.Take(MaxListed).Select(diagnostic => diagnostic.Describe()));
        string more = fresh.Count > MaxListed ? $"; and {fresh.Count - MaxListed} more" : string.Empty;
        return $"{fresh.Count} new warnings: {listed}{more}";
    }

    private static string FirstErrorLine(string output) =>
        output.Split('\n').FirstOrDefault(line => line.Contains(": error ", StringComparison.Ordinal))?.Trim() ?? "build failed";
}
