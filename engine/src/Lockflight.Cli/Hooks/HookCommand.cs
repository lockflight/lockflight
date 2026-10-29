using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Events;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// <c>lockflight hook &lt;event&gt;</c> with the hook JSON on stdin. Outside a Lockflight project every event is a silent no-op,
/// so an install never blocks unrelated work. Fails closed: an engine error blocks a tool call, and is reported but
/// does not block the events that must never brick a session (Stop, SessionStart, …). A typed in-session human command
/// (<c>/lockflight:approve</c>, <c>/lockflight:baseline</c>, <c>/lockflight:reset</c>, <c>/lockflight:amend</c>) is never passed on
/// to the agent after an error either.
/// </summary>
internal sealed class HookCommand(CliContext context) : ICommand
{
    /// <summary>The in-session human command the event expands, as far as the payload was read before an error; null for none.</summary>
    private SessionCommandSpec? expanding;

    /// <param name="arguments">The event's command-line name (<c>pre-tool-use</c>) as the one operand.</param>
    public Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken) =>
        RunAsync(HookEventNames.Parse(arguments.Rest[0]), cancellationToken);

    public async Task<ExitCode> RunAsync(HookEvent hookEvent, CancellationToken cancellationToken)
    {
        try
        {
            return await DecideAndReportAsync(hookEvent, cancellationToken);
        }
        catch (Exception exception)
        {
            // An event this engine does not know may guard a tool call: it fails closed.
            HookFailurePolicy policy = HookHandlers.For(hookEvent)?.OnEngineError(new HookFailureState(expanding is not null)) ?? HookFailurePolicy.Block;
            await context.Error.WriteLineAsync(expanding is SessionCommandSpec spec
                ? SessionCommands.InternalError(spec, exception.Message)
                : $"lockflight: internal error: {exception.Message}");
            return policy == HookFailurePolicy.Block ? ExitCode.HookBlock : ExitCode.Pass;
        }
    }

    private async Task<ExitCode> DecideAndReportAsync(HookEvent hookEvent, CancellationToken cancellationToken)
    {
        string rawInput = await context.Input.ReadToEndAsync(cancellationToken);
        // Until the payload is parsed, any mention of a command counts: an error then must not hand it to the agent.
        expanding = hookEvent == HookEvent.UserPromptExpansion ? SessionCommands.MentionedIn(rawInput) : null;
        await new HookPayloadLog(context.Time, context.Environment, context.Error).AppendAsync(hookEvent, rawInput, cancellationToken);

        HookInput? input = HookInput.Parse(rawInput);
        if (expanding is not null && input is not null)
        {
            expanding = SessionCommands.Of(input);
        }

        if (input is null)
        {
            return await ReportAsync(HookDecision.Block("invalid-hook-input", "lockflight: hook input is not valid JSON"), hookEvent, projectRoot: null, input: null, cancellationToken);
        }

        // Every typed slash command expands through UserPromptExpansion; only the in-session human commands are the engine's business.
        IHookHandler? handler = HookHandlers.For(hookEvent);
        if (handler is not null && !handler.Claims(input))
        {
            return ExitCode.Pass;
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
        if (PreconditionFailure(hookEvent) is HookDecision precondition)
        {
            return await ReportAsync(precondition, hookEvent, manifest.Root, input, cancellationToken);
        }

        // The preconditions block every event this engine does not know, so a handler exists from here on.
        HookInvocation invocation = new(input, manifest, ProjectDirectory(input), context);
        HookReply reply = await (handler ?? throw new InvalidOperationException($"no handler for {hookEvent}")).DecideAsync(invocation, cancellationToken);
        if (reply.WrittenExitCode is ExitCode written)
        {
            return written;
        }

        if (reply.Output is string output)
        {
            await context.Output.WriteLineAsync(output);
        }

        return await ReportAsync(reply.Decision, hookEvent, manifest.Root, input, cancellationToken);
    }

    /// <summary>
    /// Checks that apply to every event before it is decided: protocol compatibility and a known event. The guard's own
    /// preconditions (lockdown, engine version parity) are its first rules (<see cref="Guard.GuardRules"/>).
    /// </summary>
    private HookDecision? PreconditionFailure(HookEvent hookEvent)
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

        return null;
    }

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
