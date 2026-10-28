using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Mascot;

namespace Lockflight.Cli.Gates;

/// <summary>
/// <c>lockflight gate run [--profile edit|stop|pr] [--path dir] [--unit path[,path…]] [--base ref] [--result file]</c>. Outside a
/// Lockflight project the gates run with the default settings; an invalid manifest stops the run, so a typo in it can never weaken
/// the gates. At the root of a workspace with units the run covers every unit, or those <c>--unit</c> names.
/// </summary>
internal sealed class GateCommand(CliContext context) : ICommand
{
    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        string profileName = options.Value("--profile") ?? GateProfileNames.ToName(GateProfile.Stop);
        if (!GateProfileNames.TryParse(profileName, out GateProfile profile))
        {
            await context.Error.WriteLineAsync($"lockflight: unknown profile '{profileName}'; use edit, stop or pr");
            return ExitCode.Config;
        }

        string target = Path.GetFullPath(options.Value("--path") ?? ".", context.WorkingDirectory);
        if (!Directory.Exists(target))
        {
            await context.Error.WriteLineAsync($"lockflight: target '{target}' does not exist");
            return ExitCode.Config;
        }

        ManifestLoadResult loadResult = ManifestLocator.Discover(target);
        if (loadResult.Error is ManifestError manifestError)
        {
            await context.Error.WriteLineAsync(ManifestRequirement.Invalid(manifestError));
            return ExitCode.Config;
        }

        Manifest? manifest = loadResult.Manifest;
        GateRunContext run = manifest is null
            ? new(target, target, profile, GateSettings.Default, options.Value("--base"), context.Time)
            : GateRunContext.ForTarget(manifest, target, profile, options.Value("--base"), context.Time);
        string[] units = [.. (options.Value("--unit") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(name => name.TrimEnd('/'))];
        bool workspaceRun = manifest is { Units.Count: > 0 } && SameDirectory(target, manifest.Root);
        if (units.Length > 0 && !workspaceRun)
        {
            await context.Error.WriteLineAsync("lockflight: --unit applies at the root of a workspace with units; use --path for one directory");
            return ExitCode.Config;
        }

        if (units.FirstOrDefault(name => !manifest!.Units.Any(unit => unit.Path.Equals(name, StringComparison.OrdinalIgnoreCase))) is string unknown)
        {
            await context.Error.WriteLineAsync($"lockflight: '{unknown}' is not a unit in {Manifest.FileName}");
            return ExitCode.Config;
        }

        // A run that judges what the Stop gate judges caches its PASS the same way, so a task can be completed after it and the
        // Stop hook answers from the cache. The hash is taken before the run, as the Stop gate takes it.
        bool judgesAsTheStopGate = manifest is not null && profile == GateProfile.Stop && units.Length == 0 && options.Value("--base") is null
            && SameDirectory(target, Path.GetFullPath(manifest.StopTarget ?? ".", manifest.Root));
        string? inputsHash = judgesAsTheStopGate ? await InputsHash.ComputeAsync(manifest!.Root, cancellationToken) : null;

        long started = context.Time.GetTimestamp();
        GateResult result = workspaceRun
            ? await WorkspaceGateRunner.RunAsync(manifest!, run, units, useCache: false, context.Output, cancellationToken)
            : await GateRunner.RunAsync(run, context.Output, cancellationToken);
        long durationMs = (long)context.Time.GetElapsedTime(started).TotalMilliseconds;
        await context.Output.WriteLineAsync($"Scope: {result.Scope}");
        await context.Output.WriteLineAsync($"Result: {VerdictNames.ToContractName(result.Verdict)}");
        await MascotDisplay.DrawAsync(context, MascotDisplay.For(result.Verdict), VerdictNames.ToContractName(result.Verdict));

        if (options.Value("--result") is string resultPath)
        {
            await GateResultWriter.WriteAsync(Path.GetFullPath(resultPath, context.WorkingDirectory), result, cancellationToken);
        }

        if (manifest is not null)
        {
            LedgerGateRun gateRun = new(GateTrigger.Command, durationMs, StopCacheResult.None);
            await new LedgerWriter(manifest.Root, context.Time).AppendAsync(GateRunEvents.Completed(result, gateRun), cancellationToken);
        }

        if (inputsHash is not null && VerdictNames.AllowsNextStep(result.Verdict))
        {
            await new StopGateCache(manifest!.Root, unitPath: null).RememberPassAsync(inputsHash, cancellationToken);
        }

        return VerdictNames.ToExitCode(result.Verdict);
    }

    private static bool SameDirectory(string first, string second) =>
        Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);
}
