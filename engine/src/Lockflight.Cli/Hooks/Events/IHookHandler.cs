using Lockflight.Cli.Hooks.Requests;

namespace Lockflight.Cli.Hooks.Events;

/// <summary>The engine side of one Claude Code hook event, registered in <see cref="HookHandlers"/>.</summary>
internal interface IHookHandler
{
    HookEvent Event { get; }

    /// <summary>What an engine error means here: block (a tool call, a typed human command) or allow (never brick a session).</summary>
    HookFailurePolicy OnEngineError(HookFailureState state);

    /// <summary>Whether the payload is the engine's business at all, decided before the manifest is read.</summary>
    bool Claims(HookRequest input);

    Task<HookReply> DecideAsync(HookInvocation invocation, CancellationToken cancellationToken);
}
