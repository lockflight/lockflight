using Lockflight.Cli.Canaries;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Mascot;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// <c>lockflight doctor</c>: readiness report; exits <see cref="ExitCode.Fail"/> when any check fails. With <c>--self-test</c> it
/// runs the self-test canaries instead (<see cref="SelfTestSuite"/>). <c>--canaries</c> and <c>--canaries=full</c> are deprecated
/// names for <c>--self-test</c>, kept so an older plugin's doctor skill still works; the full canary suite moved to the engine's
/// tests.
/// </summary>
internal sealed class DoctorCommand(CliContext context) : ICommand
{
    public const string SelfTestFlag = "--self-test";

    /// <summary>Deprecated: the self-test's name before the full canary suite moved to the engine's tests.</summary>
    public const string CanariesFlag = "--canaries";

    /// <summary>Deprecated: as <see cref="CanariesFlag"/>; nothing is built any more.</summary>
    public const string FullCanariesFlag = "--canaries=full";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        if (options.Flag(CanariesFlag) || options.Flag(FullCanariesFlag))
        {
            await context.Error.WriteLineAsync(
                $"lockflight: {(options.Flag(FullCanariesFlag) ? FullCanariesFlag : CanariesFlag)} is deprecated; it runs {SelfTestFlag}. "
                + "The full canary suite is part of the engine's tests and runs in CI.");
        }

        if (options.Flag(SelfTestFlag) || options.Flag(CanariesFlag) || options.Flag(FullCanariesFlag))
        {
            await context.Error.WriteLineAsync("lockflight: planting the self-test canaries in a temporary scratch project…");
            return await SelfTestSuite.RunAndReportAsync(context.Time, context.Output, cancellationToken);
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
