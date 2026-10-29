using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>Vulnerable NuGet packages (direct and transitive): new High/Critical advisories outside the accepted baseline fail.</summary>
internal sealed class DependencyStep : IGateStep
{
    public const string StepName = "security.deps";

    private const int MaxListed = 3;

    public string Name => StepName;

    public StepScope Scope => StepScope.Unit;

    public string CacheKey => StepName;

    public async Task<GateStepResult> RunAsync(GateRunContext run, GateRunFacts facts, CancellationToken cancellationToken) =>
        new((await ScanAsync(run, cancellationToken)).Step, facts);

    /// <summary>The verdict with every vulnerable package found, which <see cref="Baselines.BaselineRun"/> records as a baseline.</summary>
    public static async Task<DependencyScan> ScanAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        ProcessResult listing = await run.Processes(
            "dotnet", ["package", "list", .. (run.BuildFile is null ? [] : new[] { "--project", run.BuildFile }), "--vulnerable", "--include-transitive", "--format", "json"], run.Target, run.TimeLeft(), cancellationToken);
        if (StepHealth.Unhealthy(StepName, listing, run) is GateStep unhealthy)
        {
            return new DependencyScan(unhealthy, []);
        }

        int jsonStart = listing.Output.IndexOf('{', StringComparison.Ordinal);
        if (listing.ExitCode != 0 || jsonStart < 0)
        {
            return new DependencyScan(new GateStep(StepName, listing.ExitCode, Verdict.Error, listing.DurationMs, $"dotnet package list exited {listing.ExitCode} without a report"), []);
        }

        IReadOnlyList<VulnerablePackage> found = VulnerablePackages.Parse(listing.Output[jsonStart..]);
        IReadOnlySet<string> accepted = new AcceptedAdvisories(run.ProjectRoot, run.UnitPath).Load();
        List<VulnerablePackage> blocking = [.. found.Where(package => package.IsBlocking && !accepted.Contains(package.Fingerprint))];
        int reportedOnly = found.Count(package => !package.IsBlocking && !accepted.Contains(package.Fingerprint));

        GateStep step = blocking.Count > 0
            ? new GateStep(StepName, listing.ExitCode, Verdict.Fail, listing.DurationMs, $"{blocking.Count} new High/Critical advisories: {string.Join("; ", blocking.Take(MaxListed).Select(package => package.Describe()))}")
            : new GateStep(StepName, listing.ExitCode, Verdict.Pass, listing.DurationMs, reportedOnly > 0 ? $"{reportedOnly} Moderate/Low advisories reported" : string.Empty);
        return new DependencyScan(step, found);
    }
}
