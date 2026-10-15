using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Mascot;

namespace Lockflight.Cli.Doctor;

/// <summary><c>lockflight doctor</c>: readiness report; exits <see cref="ExitCode.Fail"/> when any check fails.</summary>
internal sealed class DoctorCommand(CliContext context)
{
    public async Task<ExitCode> RunAsync(CancellationToken cancellationToken)
    {
        string root = ManifestLocator.Discover(context.WorkingDirectory) is { Manifest: Manifest manifest } ? manifest.Root : context.WorkingDirectory;
        IReadOnlyList<DoctorCheck> checks = await ReadinessChecks.RunAsync(
            root, context.Environment.HomeDirectory(), context.Environment.Get(EnvironmentVariables.NuGetPackages), context.Time, cancellationToken);

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
