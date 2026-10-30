using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Mascot;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Gates;

/// <summary>
/// <c>lockflight gate run [--profile edit|stop|pr] [--path dir] [--unit path[,path…]] [--base ref] [--result file] [--full] [--shell dir]</c>.
/// Outside a Lockflight project the gates run with the default settings; an invalid manifest stops the run, so a typo in it can never
/// weaken the gates. At the root of a workspace with units the run covers every unit, or those <c>--unit</c> names. The stop profile
/// builds and tests the projects the changes since its last PASS affect; <c>--full</c> makes it build and test every project. In a unit
/// repository of a workspace shell (<c>shell:</c>) the run judges with the shell's policy for the unit too, read from the clone
/// <c>--shell</c> names or from the shell the unit is checked out in; a pr run cannot go without it.
/// </summary>
internal sealed class GateCommand(CliContext context) : ICommand
{
    public const string ShellOption = "--shell";

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

        bool full = options.Flag("--full");
        if (full && profile != GateProfile.Stop)
        {
            await context.Error.WriteLineAsync("lockflight: --full applies to the stop profile; the edit profile judges changed lines only and the pr profile always runs every project");
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
        ShellPolicyApplication shell = await ApplyShellPolicyAsync(manifest, options, profile, cancellationToken);
        if (shell.Error is string shellError)
        {
            await context.Error.WriteLineAsync($"lockflight: {shellError}");
            return shell.Failure;
        }

        manifest = shell.Manifest;
        GateRunContext run = manifest is null
            ? new(target, target, profile, GateSettings.Default, options.Value("--base"), context.Time)
            : GateRunContext.ForTarget(manifest, target, profile, options.Value("--base"), context.Time);
        run = run with { FullRun = full, Policy = shell.Policy is null ? null : manifest };
        if (shell.Policy is ShellPolicy policy)
        {
            await context.Output.WriteLineAsync($"Policy: {Manifest.FileName} with the workspace shell's policy for unit {policy.Unit.Path} ({manifest!.Shell!.Url} at {CommitIds.Short(policy.Commit)})");
        }

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
            string? baseCommit = options.Value("--base") is string baseRef ? await Git.CommitOfAsync(target, baseRef, cancellationToken) : null;
            await GateResultWriter.WriteAsync(
                Path.GetFullPath(resultPath, context.WorkingDirectory), result, run.Settings, ToolPin.RunningVersion, baseCommit,
                await Git.HeadAsync(target, cancellationToken), context.Time.GetUtcNow(), cancellationToken);
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

    /// <summary>
    /// The manifest the run judges with: a unit repository's own, made at least as strict as its workspace shell's policy when the
    /// shell can be read (<c>--shell</c>, else the shell the unit is checked out in). A pr run against a base in a unit whose shell
    /// cannot be found fails rather than judge without the shell's policy.
    /// </summary>
    private async Task<ShellPolicyApplication> ApplyShellPolicyAsync(Manifest? manifest, CommandOptions options, GateProfile profile, CancellationToken cancellationToken)
    {
        string? given = options.Value(ShellOption) is string option ? Path.GetFullPath(option, context.WorkingDirectory) : null;
        if (manifest?.Shell is not ShellSettings pointer)
        {
            return new ShellPolicyApplication(manifest, Policy: null, Error: null, ExitCode.Pass);
        }

        string? directory = given ?? ShellLocator.FindEnclosing(manifest.Root, pointer);
        if (directory is null)
        {
            return profile == GateProfile.PullRequest
                ? new ShellPolicyApplication(manifest, Policy: null,
                    $"this repository is a unit of the workspace shell {pointer.Url}, whose policy judges its pull requests; clone the shell with `lockflight workspace clone --into <dir>` (outside the repository) and pass {ShellOption} <dir>",
                    ExitCode.Config)
                : new ShellPolicyApplication(manifest, Policy: null, Error: null, ExitCode.Pass);
        }

        ShellPolicyReading reading = await ShellPolicyReader.ReadAsync(directory, pointer, checkOrigin: given is not null, cancellationToken);
        return reading.Policy is ShellPolicy policy
            ? new ShellPolicyApplication(ShellPolicyOverlay.Apply(manifest, policy), policy, Error: null, ExitCode.Pass)
            : new ShellPolicyApplication(manifest, Policy: null, reading.Error, ExitCode.Error);
    }

    private static bool SameDirectory(string first, string second) =>
        Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);
}
