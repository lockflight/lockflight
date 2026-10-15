using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Updating;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// <c>lockflight hook &lt;event&gt;</c> with the hook JSON on stdin. Outside a Lockflight project every event is a silent no-op,
/// so an install never blocks unrelated work. Fails closed: an engine error blocks a tool call, and is reported but
/// does not block the events that must never brick a session (Stop, SessionStart, …).
/// </summary>
internal sealed class HookCommand(CliContext context)
{
    public async Task<ExitCode> RunAsync(HookEvent hookEvent, CancellationToken cancellationToken)
    {
        try
        {
            return await DecideAndReportAsync(hookEvent, cancellationToken);
        }
        catch (Exception exception)
        {
            await context.Error.WriteLineAsync($"lockflight: internal error: {exception.Message}");
            bool guardsAToolCall = hookEvent is HookEvent.PreToolUse or HookEvent.Unknown;
            return guardsAToolCall ? ExitCode.HookBlock : ExitCode.Pass;
        }
    }

    private async Task<ExitCode> DecideAndReportAsync(HookEvent hookEvent, CancellationToken cancellationToken)
    {
        string rawInput = await context.Input.ReadToEndAsync(cancellationToken);
        await new HookPayloadLog(context.Time, context.Environment, context.Error).AppendAsync(hookEvent, rawInput, cancellationToken);

        HookInput? input = HookInput.Parse(rawInput);
        if (input is null)
        {
            return await ReportAsync(HookDecision.Block("invalid-hook-input", "lockflight: hook input is not valid JSON"), hookEvent, projectRoot: null, input: null, cancellationToken);
        }

        ManifestLoadResult loadResult = ManifestLocator.Discover(ProjectDirectory(input));
        if (loadResult.Status == ManifestLoadStatus.NotFound)
        {
            return ExitCode.Pass;
        }

        if (loadResult.Error is ManifestError manifestError)
        {
            return await ReportAsync(InvalidManifestPolicy.Decide(hookEvent, input, manifestError), hookEvent, Path.GetDirectoryName(manifestError.Path), input, cancellationToken);
        }

        Manifest manifest = loadResult.Manifest!;
        if (PreconditionFailure(hookEvent, input, manifest) is HookDecision precondition)
        {
            return await ReportAsync(precondition, hookEvent, manifest.Root, input, cancellationToken);
        }

        HookDecision decision = hookEvent switch
        {
            HookEvent.PreToolUse => await new ProtectedPathGuard(context.Error, context.Environment).DecideAsync(input, manifest),
            HookEvent.Stop => await StopGate.DecideAsync(manifest, context.Time, cancellationToken),
            _ => HookDecision.Allow,
        };

        if (hookEvent == HookEvent.SessionStart)
        {
            await context.Output.WriteLineAsync(SessionBanner.ToHookOutput(manifest, ProjectDirectory(input), UpdateNotice()));
        }

        return await ReportAsync(decision, hookEvent, manifest.Root, input, cancellationToken);
    }

    /// <summary>Checks that apply before any guard: protocol compatibility, known event, lockdown, engine version parity.</summary>
    private HookDecision? PreconditionFailure(HookEvent hookEvent, HookInput input, Manifest manifest)
    {
        if (PluginHandshake.IncompatibleApi(context.Environment) is string announcedApi)
        {
            return HookDecision.Block(
                "plugin-api-mismatch",
                $"lockflight: the plugin speaks hook API {announcedApi}, this engine speaks {PluginHandshake.SupportedApi}. Update the plugin or run: {RecoveryCommands.RestoreAndLink}");
        }

        if (hookEvent == HookEvent.Unknown)
        {
            return HookDecision.Block("unknown-hook-event", "lockflight: this engine does not know the hook event it was called for; update it with: " + RecoveryCommands.RestoreAndLink);
        }

        if (hookEvent != HookEvent.PreToolUse)
        {
            return null;
        }

        if (LockdownPolicy.IsActive(manifest.Root))
        {
            HookDecision lockdown = LockdownPolicy.Decide(input);
            return lockdown.Outcome == HookOutcome.Block ? lockdown : null;
        }

        return PinMismatch(input, manifest);
    }

    /// <summary>
    /// Gate parity: a stale linked binary must not judge the project with old rules. Like recovery mode in the shim, reading and the
    /// exact restore/link command stay allowed so the agent can repair it; a tool manifest that cannot be read blocks the same way.
    /// </summary>
    private static HookDecision? PinMismatch(HookInput input, Manifest manifest)
    {
        ToolPinReading pin = ToolPin.ReadPin(manifest.Root);
        bool mismatch = pin.Status == ToolPinStatus.Unreadable
            || (pin.Status == ToolPinStatus.Pinned && !ToolPin.Matches(pin.Version!, ToolPin.RunningVersion));
        if (!mismatch || ReadOnlyTools.Allows(input) || RecoveryCommands.Allows(input))
        {
            return null;
        }

        string reason = pin.Status == ToolPinStatus.Unreadable
            ? $"lockflight: the engine pin cannot be read: {pin.Problem}. Fix dotnet-tools.json (a protected file: a human edit), then run exactly: {RecoveryCommands.RestoreAndLink}"
            : $"lockflight: running engine {ToolPin.RunningVersion} but the project pins {pin.Version}. Run exactly: {RecoveryCommands.RestoreAndLink}";
        return HookDecision.Block("stale-engine", reason);
    }

    /// <summary>
    /// For the person, at session start: a newer engine is installed than the one this project runs (after an install from source
    /// in another project, or a pull). Null when they match or there is no install from source.
    /// </summary>
    private string? UpdateNotice() =>
        context.Environment.HomeDirectory() is string home && UserInstall.InstalledVersion(home) is string installed
        && !ToolPin.Matches(installed, ToolPin.RunningVersion)
            ? $"Lockflight {installed} is installed; this project runs {ToolPin.RunningVersion}. Run {EngineCommand.UserInstall} update in the project {HumanOnlyCommands.WhereToRun}."
            : null;

    /// <summary>CLAUDE_PROJECT_DIR stays at the session's root after `cd` or entering a worktree; the payload cwd does not.</summary>
    private string ProjectDirectory(HookInput input) =>
        context.Environment.Get(EnvironmentVariables.ClaudeProjectDirectory) is string projectDirectory && projectDirectory.Length > 0
            ? projectDirectory
            : input.WorkingDirectory ?? context.WorkingDirectory;

    private async Task<ExitCode> ReportAsync(HookDecision decision, HookEvent hookEvent, string? projectRoot, HookInput? input, CancellationToken cancellationToken)
    {
        if (decision.Outcome == HookOutcome.Allow)
        {
            return ExitCode.Pass;
        }

        await context.Error.WriteLineAsync(decision.Reason);
        if (projectRoot is not null)
        {
            LedgerEvent blocked = new(LedgerEventType.HookBlocked, $"{HookEventNames.ToName(hookEvent)}:{decision.Code}", decision.Code ?? "blocked", decision.Reason)
            {
                Context = input is null ? null : new LedgerEventContext(input.ToolName, input.Command ?? input.FilePath, input.SessionId, input.Agent ?? "main"),
            };
            await new LedgerWriter(projectRoot, context.Time).AppendAsync(blocked, cancellationToken);
        }

        return ExitCode.HookBlock;
    }
}
