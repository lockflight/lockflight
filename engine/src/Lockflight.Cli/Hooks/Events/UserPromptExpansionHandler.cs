using System.Text;
using System.Text.Json;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Hooks.Events;

/// <summary>
/// UserPromptExpansion: every typed slash command expands through it, and only the in-session human commands
/// (<see cref="SessionCommands"/>) are the engine's business. A refusal or the digest goes to stderr with exit 2, which Claude Code
/// shows the person and keeps from the agent; a recorded decision lets the command run, with a confirmation for the person and a
/// note for the agent. An engine error blocks when the payload named such a command, so it never reaches the agent.
/// </summary>
internal sealed class UserPromptExpansionHandler : IHookHandler
{
    public HookEvent Event => HookEvent.UserPromptExpansion;

    public HookFailurePolicy OnEngineError(HookFailureState state) => state.ExpandingSessionCommand ? HookFailurePolicy.Block : HookFailurePolicy.Allow;

    public bool Claims(HookRequest input) => SessionCommands.Of(input) is not null;

    public async Task<HookReply> DecideAsync(HookInvocation invocation, CancellationToken cancellationToken)
    {
        SessionCommandSpec spec = SessionCommands.Of(invocation.Input) ?? throw new InvalidOperationException("the expansion names no in-session command");
        SessionCommandResult result;
        try
        {
            result = await SessionCommands.DecideAsync(spec, invocation.Input, invocation.Context, invocation.Manifest, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Failing open here would hand the command to the agent; nothing is recorded either way, so the person sees why.
            result = SessionCommandResult.Refused($"{spec.RefusalPrefix}:internal-error", SessionCommands.InternalError(spec, exception.Message));
        }

        switch (result.Outcome)
        {
            case SessionCommandOutcome.Refused:
                return HookReply.Of(HookDecision.Block(result.Code!, result.Message!));
            case SessionCommandOutcome.Answered:
                await invocation.Context.Error.WriteLineAsync(result.Message);
                await new LedgerWriter(invocation.Manifest.Root, invocation.Context.Time).AppendAsync(
                    new LedgerEvent(LedgerEventType.SessionCommandAnswered, $"{HookEventNames.ToName(HookEvent.UserPromptExpansion)}:{result.Code}", result.Code!, null),
                    cancellationToken);
                return HookReply.Written(ExitCode.HookBlock);
            case SessionCommandOutcome.Shown:
                await invocation.Context.Error.WriteLineAsync(result.Message);
                return HookReply.Written(ExitCode.HookBlock);
            case SessionCommandOutcome.Recorded:
                return new HookReply(HookDecision.Allow, ToExpansionOutput(result.Message!, result.AgentContext!));
            default:
                return HookReply.Of(HookDecision.Allow);
        }
    }

    private static string ToExpansionOutput(string userMessage, string agentContext)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("systemMessage", userMessage);
            writer.WriteStartObject("hookSpecificOutput");
            writer.WriteString("hookEventName", "UserPromptExpansion");
            writer.WriteString("additionalContext", agentContext);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
