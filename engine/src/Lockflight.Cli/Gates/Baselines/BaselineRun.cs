using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates.Baselines;

/// <summary>
/// Takes a baseline in two parts, so a person can see what it accepts before it is written: <see cref="CaptureAsync"/> runs what the
/// baseline is taken from (a full build, the tests, the advisory listing) and returns the entries; <see cref="SaveAsync"/> writes them
/// and ledgers it. <c>lockflight baseline</c> does both at once; <c>/lockflight:baseline</c> shows the entries in between.
/// </summary>
internal sealed class BaselineRun(CliContext context, Manifest manifest)
{
    /// <param name="underHook">Whether a hook with a time limit runs this: the test run's budget is then capped as under the Stop hook.</param>
    public async Task<BaselineCapture> CaptureAsync(BaselineKind kind, string target, TimeSpan timeLimit, bool underHook, CancellationToken cancellationToken) => kind switch
    {
        BaselineKind.Tests => await FailingTestsAsync(target, underHook, cancellationToken),
        BaselineKind.Dependencies => await AdvisoriesAsync(target, cancellationToken),
        _ => await WarningsAsync(target, timeLimit, cancellationToken),
    };

    /// <summary>The baseline file relative to the project root.</summary>
    public string RelativePath(BaselineKind kind, string? unitPath) => kind switch
    {
        BaselineKind.Tests => new KnownFailingTests(manifest.Root, unitPath).RelativePath,
        BaselineKind.Dependencies => new AcceptedAdvisories(manifest.Root, unitPath).RelativePath,
        _ => new DiagnosticsBaseline(manifest.Root, unitPath).RelativePath,
    };

    /// <summary>The entries the baseline accepts now (empty when there is none).</summary>
    public IReadOnlyList<string> Current(BaselineKind kind, string? unitPath) => kind switch
    {
        BaselineKind.Tests => [.. new KnownFailingTests(manifest.Root, unitPath).Load()],
        BaselineKind.Dependencies => [.. new AcceptedAdvisories(manifest.Root, unitPath).Load()],
        _ => new DiagnosticsBaseline(manifest.Root, unitPath).Accepted(),
    };

    /// <summary>Writes the entries as the kind's baseline and ledgers it; returns the line that tells the person.</summary>
    public async Task<string> SaveAsync(BaselineKind kind, string? unitPath, IReadOnlyList<string> entries, CancellationToken cancellationToken)
    {
        int count = kind == BaselineKind.Build ? entries.Count : entries.Distinct(StringComparer.Ordinal).Count();
        switch (kind)
        {
            case BaselineKind.Tests:
                new KnownFailingTests(manifest.Root, unitPath).Save(entries);
                break;
            case BaselineKind.Dependencies:
                new AcceptedAdvisories(manifest.Root, unitPath).Save(entries);
                break;
            default:
                new DiagnosticsBaseline(manifest.Root, unitPath).Save(entries);
                break;
        }

        string name = BaselineKindNames.ToName(kind);
        string accepted = kind switch
        {
            BaselineKind.Build => $"{count} warnings accepted",
            BaselineKind.Tests => $"{count} failing {(count == 1 ? "test" : "tests")} accepted",
            _ => $"{count} advisories accepted",
        };
        await new LedgerWriter(manifest.Root, context.Time).AppendAsync(new LedgerEvent(LedgerEventType.ConfigWritten, $"baseline:{name}", accepted, null), cancellationToken);
        return $"baseline: {accepted} -> {RelativePath(kind, unitPath)}";
    }

    private async Task<BaselineCapture> WarningsAsync(string target, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        // A full rebuild: an incremental build skips unchanged projects and would leave their warnings out of the baseline.
        GateRunContext buildRun = GateRunContext.ForTarget(manifest, target, GateProfile.Stop, baseRef: null, context.Time, underStopHook: false);
        ProcessResult build = await ProcessRunner.RunAsync(
            "dotnet", ["build", .. buildRun.BuildFileArguments, "--no-incremental", "--nologo", "--interactive", "false"], target, timeLimit, cancellationToken);
        if (build.ExitCode != 0)
        {
            return BaselineCapture.Failed(build.TimedOut ? $"the build did not finish within {timeLimit.TotalSeconds:0} seconds" : "the build fails; a baseline is taken from a building tree only", ExitCode.Fail);
        }

        IReadOnlyList<BuildDiagnostic> diagnostics = BuildDiagnostics.Parse(build.Output, manifest.Root);
        return BaselineCapture.Taken([.. diagnostics.Select(diagnostic => diagnostic.Fingerprint)], buildRun.UnitPath);
    }

    private async Task<BaselineCapture> FailingTestsAsync(string target, bool underHook, CancellationToken cancellationToken)
    {
        GateRunContext run = GateRunContext.ForTarget(manifest, target, GateProfile.Stop, baseRef: null, context.Time, underStopHook: underHook);
        if ((await BuildStep.RunAsync(run, cancellationToken)).Verdict != Verdict.Pass)
        {
            return BaselineCapture.Failed("the build gate does not pass; fix it before accepting failing tests", ExitCode.Fail);
        }

        TestStepOutcome tests = await TestStep.RunAsync(run, cancellationToken);
        return tests.Step.Verdict == Verdict.Error
            ? BaselineCapture.Failed($"the test run itself broke ({tests.Step.Detail}); a baseline needs a test run that completes", ExitCode.Error)
            : BaselineCapture.Taken(tests.FailedTests, run.UnitPath);
    }

    private async Task<BaselineCapture> AdvisoriesAsync(string target, CancellationToken cancellationToken)
    {
        GateRunContext run = GateRunContext.ForTarget(manifest, target, GateProfile.PullRequest, baseRef: null, context.Time);
        (GateStep step, IReadOnlyList<VulnerablePackage> found) = await DependencyStep.RunAsync(run, cancellationToken);
        return step.Verdict == Verdict.Error
            ? BaselineCapture.Failed($"could not list vulnerable packages ({step.Detail})", ExitCode.Error)
            : BaselineCapture.Taken([.. found.Select(package => package.Fingerprint)], run.UnitPath);
    }
}
