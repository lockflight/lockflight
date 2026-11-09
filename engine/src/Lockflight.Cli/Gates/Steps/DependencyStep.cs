using System.Text.Json;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>Vulnerable NuGet packages (direct and transitive): new High/Critical advisories outside the accepted baseline fail.</summary>
internal sealed class DependencyStep : IGateStep
{
    public const string StepName = "security.deps";

    private const int MaxListed = 3;

    private static readonly string[] ListingOptions = ["--vulnerable", "--include-transitive", "--format", "json"];

    public string Name => StepName;

    public StepScope Scope => StepScope.Unit;

    public string CacheKey => StepName;

    public async Task<GateStepResult> RunAsync(GateRunContext run, GateRunFacts facts, CancellationToken cancellationToken) =>
        new((await ScanAsync(run, cancellationToken)).Step, facts);

    /// <summary>
    /// The verdict with every vulnerable package found, which <see cref="Baselines.BaselineRun"/> records as a baseline. The listing
    /// uses the .NET 10 command (<c>dotnet package list</c>); an older SDK chosen by <c>global.json</c> does not have it, so when that
    /// gives no report the step asks again with the command SDK 7.0.200 and later understand (<c>dotnet list package</c>).
    /// </summary>
    public static async Task<DependencyScan> ScanAsync(GateRunContext run, CancellationToken cancellationToken)
    {
        ProcessResult listing = await CompleteOutput.RunAsync(() => run.Processes("dotnet", NounFirstArguments(run), run.Target, run.TimeLeft(), cancellationToken));
        string noReport = $"dotnet package list exited {listing.ExitCode} without a report";
        if (!listing.TimedOut && !listing.OutputHeld && !HasReport(listing))
        {
            ProcessResult olderSdkListing = await CompleteOutput.RunAsync(() => run.Processes("dotnet", VerbFirstArguments(run), run.Target, run.TimeLeft(), cancellationToken));
            noReport = $"dotnet package list exited {listing.ExitCode} and dotnet list package exited {olderSdkListing.ExitCode}, both without a report";
            listing = olderSdkListing with { DurationMs = listing.DurationMs + olderSdkListing.DurationMs };
        }

        if (StepHealth.Unhealthy(StepName, listing, run) is GateStep unhealthy)
        {
            return new DependencyScan(unhealthy, []);
        }

        if (listing.OutputHeld)
        {
            return new DependencyScan(new GateStep(StepName, listing.ExitCode, Verdict.Error, ReasonCode.ToolError, listing.DurationMs, CompleteOutput.Problem("the package listing")), []);
        }

        if (!HasReport(listing))
        {
            return new DependencyScan(new GateStep(StepName, listing.ExitCode, Verdict.Error, ReasonCode.ToolError, listing.DurationMs, noReport), []);
        }

        int jsonStart = listing.Output.IndexOf('{', StringComparison.Ordinal);
        IReadOnlyList<VulnerablePackage> found;
        try
        {
            found = VulnerablePackages.Parse(listing.Output[jsonStart..]);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new DependencyScan(
                new GateStep(StepName, listing.ExitCode, Verdict.Error, ReasonCode.ToolError, listing.DurationMs, $"the package listing's report could not be read: {exception.Message}"), []);
        }

        IReadOnlySet<string> accepted = new AcceptedAdvisories(run.ProjectRoot, run.UnitPath).Load();
        List<VulnerablePackage> blocking = [.. found.Where(package => package.IsBlocking && !accepted.Contains(package.Fingerprint))];
        int reportedOnly = found.Count(package => !package.IsBlocking && !accepted.Contains(package.Fingerprint));

        GateStep step = blocking.Count > 0
            ? new GateStep(StepName, listing.ExitCode, Verdict.Fail, ReasonCode.VulnerablePackage, listing.DurationMs, $"{blocking.Count} new High/Critical advisories: {string.Join("; ", blocking.Take(MaxListed).Select(package => package.Describe()))}")
            : new GateStep(StepName, listing.ExitCode, Verdict.Pass, ReasonCode.None, listing.DurationMs, reportedOnly > 0 ? $"{reportedOnly} Moderate/Low advisories reported" : string.Empty);
        return new DependencyScan(step, found);
    }

    private static string[] NounFirstArguments(GateRunContext run) =>
        ["package", "list", .. run.BuildFileArguments.SelectMany(file => new[] { "--project", file }), .. ListingOptions];

    private static string[] VerbFirstArguments(GateRunContext run) => ["list", .. run.BuildFileArguments, "package", .. ListingOptions];

    private static bool HasReport(ProcessResult listing) => listing.ExitCode == 0 && listing.Output.Contains('{', StringComparison.Ordinal);
}
