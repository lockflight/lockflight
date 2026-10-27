using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Baselines;

/// <summary>
/// <c>lockflight baseline build|tests|deps [--path dir]</c>: accept the current build warnings, failing tests or advisories of a
/// brownfield project. A human decision — the guard blocks this command inside agent sessions, and CI reads baselines from the base branch.
/// </summary>
internal sealed class BaselineCommand(CliContext context)
{
    private static readonly string[] ValueOptions = ["--path"];

    public async Task<ExitCode> RunAsync(string kind, string[] args, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync($"baseline {kind}", args, ValueOptions, [], context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        string target = Path.GetFullPath(options.Value("--path") ?? ".", context.WorkingDirectory);
        if (await ManifestRequirement.LoadAsync(context, target, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        if (kind == "tests")
        {
            return await AcceptFailingTestsAsync(target, manifest, cancellationToken);
        }

        if (kind == "deps")
        {
            return await AcceptAdvisoriesAsync(target, manifest, cancellationToken);
        }

        // A full rebuild: an incremental build skips unchanged projects and would leave their warnings out of the baseline.
        GateRunContext buildRun = GateRunContext.ForTarget(manifest, target, GateProfile.Stop, baseRef: null, context.Time, underStopHook: false);
        ProcessResult build = await ProcessRunner.RunAsync(
            "dotnet", ["build", .. buildRun.BuildFileArguments, "--no-incremental", "--nologo", "--interactive", "false"], target, cancellationToken);
        if (build.ExitCode != 0)
        {
            await context.Error.WriteLineAsync("lockflight: the build fails; a baseline is taken from a building tree only");
            return ExitCode.Fail;
        }

        IReadOnlyList<BuildDiagnostic> diagnostics = BuildDiagnostics.Parse(build.Output, manifest.Root);
        DiagnosticsBaseline baseline = new(manifest.Root, buildRun.UnitPath);
        baseline.Save(diagnostics);
        await new LedgerWriter(manifest.Root, context.Time).AppendAsync(
            new LedgerEvent(LedgerEventType.ConfigWritten, "baseline:build", $"{diagnostics.Count} warnings accepted", null), cancellationToken);
        await context.Output.WriteLineAsync($"baseline: {diagnostics.Count} warnings accepted -> {baseline.RelativePath}");
        return ExitCode.Pass;
    }

    private async Task<ExitCode> AcceptFailingTestsAsync(string target, Manifest manifest, CancellationToken cancellationToken)
    {
        GateRunContext run = GateRunContext.ForTarget(manifest, target, GateProfile.Stop, baseRef: null, context.Time, underStopHook: false);
        if ((await BuildStep.RunAsync(run, cancellationToken)).Verdict != Verdict.Pass)
        {
            await context.Error.WriteLineAsync("lockflight: the build gate does not pass; fix it before accepting failing tests");
            return ExitCode.Fail;
        }

        TestStepOutcome tests = await TestStep.RunAsync(run, cancellationToken);
        if (tests.Step.Verdict == Verdict.Error)
        {
            await context.Error.WriteLineAsync($"lockflight: the test run itself broke ({tests.Step.Detail}); a baseline needs a test run that completes");
            return ExitCode.Error;
        }

        KnownFailingTests known = new(manifest.Root, run.UnitPath);
        known.Save(tests.FailedTests);
        string accepted = $"{tests.FailedTests.Count} failing {(tests.FailedTests.Count == 1 ? "test" : "tests")} accepted";
        await new LedgerWriter(manifest.Root, context.Time).AppendAsync(
            new LedgerEvent(LedgerEventType.ConfigWritten, "baseline:tests", accepted, null), cancellationToken);
        await context.Output.WriteLineAsync($"baseline: {accepted} -> {known.RelativePath}");
        return ExitCode.Pass;
    }

    private async Task<ExitCode> AcceptAdvisoriesAsync(string target, Manifest manifest, CancellationToken cancellationToken)
    {
        GateRunContext run = GateRunContext.ForTarget(manifest, target, GateProfile.PullRequest, baseRef: null, context.Time);
        (GateStep step, IReadOnlyList<VulnerablePackage> found) = await DependencyStep.RunAsync(run, cancellationToken);
        if (step.Verdict == Verdict.Error)
        {
            await context.Error.WriteLineAsync($"lockflight: could not list vulnerable packages ({step.Detail})");
            return ExitCode.Error;
        }

        AcceptedAdvisories accepted = new(manifest.Root, run.UnitPath);
        accepted.Save(found);
        await new LedgerWriter(manifest.Root, context.Time).AppendAsync(
            new LedgerEvent(LedgerEventType.ConfigWritten, "baseline:deps", $"{found.Count} advisories accepted", null), cancellationToken);
        await context.Output.WriteLineAsync($"baseline: {found.Count} advisories accepted -> {accepted.RelativePath}");
        return ExitCode.Pass;
    }
}
