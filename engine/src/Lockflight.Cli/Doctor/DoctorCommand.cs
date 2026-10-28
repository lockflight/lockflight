using Lockflight.Cli.Canaries;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Mascot;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// <c>lockflight doctor</c>: readiness report; exits <see cref="ExitCode.Fail"/> when any check fails. With <c>--canaries</c> (or
/// <c>--canaries=full</c>) it runs the canary self-test instead (<see cref="CanarySuite"/>).
/// </summary>
internal sealed class DoctorCommand(CliContext context) : ICommand
{
    public const string CanariesFlag = "--canaries";

    public const string FullCanariesFlag = "--canaries=full";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        if (options.Flag(CanariesFlag) || options.Flag(FullCanariesFlag))
        {
            CanaryMode mode = options.Flag(FullCanariesFlag) ? CanaryMode.Full : CanaryMode.Fast;
            await context.Error.WriteLineAsync($"lockflight: planting canaries in a temporary scratch project{(mode == CanaryMode.Full ? " (full: builds and runs a generated project)" : string.Empty)}…");
            return await CanarySuite.ReportAsync(await CanarySuite.RunAsync(mode, context.Time, cancellationToken), mode, context.Output);
        }

        string root = ManifestLocator.Discover(context.WorkingDirectory) is { Manifest: Manifest manifest } ? manifest.Root : context.WorkingDirectory;
        IReadOnlyList<DoctorCheck> checks = await ReadinessChecks.RunAsync(
            root, context.Environment.HomeDirectory(), context.Environment.Get(EnvironmentVariables.NuGetPackages),
            Executables.Find("gitleaks", context.Environment.Get(EnvironmentVariables.Gitleaks)), context.Environment, context.Time, cancellationToken);

        foreach (DoctorCheck check in checks)
        {
            string fix = check.Fix is null ? string.Empty : $"  → {check.Fix}";
            await context.Output.WriteLineAsync($"{Label(check.Status),-5} {check.Name}: {check.Detail}{fix}");
        }

        bool failed = checks.Any(check => check.Status == CheckStatus.Fail);
        await MascotDisplay.DrawAsync(context, failed ? MascotPose.Fail : MascotPose.Pass, failed ? "Not ready yet: fix the FAIL lines above." : "Ready. Levi is on duty.");
        return failed ? ExitCode.Fail : ExitCode.Pass;
    }

    private static string Label(CheckStatus status) => status switch
    {
        CheckStatus.Pass => "ok",
        CheckStatus.Warn => "warn",
        _ => "FAIL",
    };
}
