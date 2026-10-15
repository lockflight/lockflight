using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>Vulnerable NuGet packages (direct and transitive): new High/Critical advisories outside the accepted baseline fail.</summary>
internal static class DependencyStep
{
    public const string Name = "security.deps";

    private const int MaxListed = 3;

    public static async Task<(GateStep Step, IReadOnlyList<VulnerablePackage> Found)> RunAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        ProcessResult listing = await ProcessRunner.RunAsync(
            "dotnet", ["package", "list", .. (run.BuildFile is null ? [] : new[] { "--project", run.BuildFile }), "--vulnerable", "--include-transitive", "--format", "json"], run.Target, run.TimeLeft(), cancellationToken);
        if (StepHealth.Unhealthy(Name, listing, run) is GateStep unhealthy)
        {
            return (unhealthy, []);
        }

        int jsonStart = listing.Output.IndexOf('{', StringComparison.Ordinal);
        if (listing.ExitCode != 0 || jsonStart < 0)
        {
            return (new GateStep(Name, listing.ExitCode, Verdict.Error, listing.DurationMs, $"dotnet package list exited {listing.ExitCode} without a report"), []);
        }

        IReadOnlyList<VulnerablePackage> found = VulnerablePackages.Parse(listing.Output[jsonStart..]);
        IReadOnlySet<string> accepted = new AcceptedAdvisories(run.ProjectRoot, run.UnitPath).Load();
        List<VulnerablePackage> blocking = [.. found.Where(package => package.IsBlocking && !accepted.Contains(package.Fingerprint))];
        int reportedOnly = found.Count(package => !package.IsBlocking && !accepted.Contains(package.Fingerprint));

        GateStep step = blocking.Count > 0
            ? new GateStep(Name, listing.ExitCode, Verdict.Fail, listing.DurationMs, $"{blocking.Count} new High/Critical advisories: {string.Join("; ", blocking.Take(MaxListed).Select(package => package.Describe()))}")
            : new GateStep(Name, listing.ExitCode, Verdict.Pass, listing.DurationMs, reportedOnly > 0 ? $"{reportedOnly} Moderate/Low advisories reported" : string.Empty);
        return (step, found);
    }
}
