namespace Lockflight.Cli.Hooks.Events;

/// <summary>An event the engine records in the payload log only (<c>PostToolUse</c>, <c>SubagentStop</c>): always allowed.</summary>
internal sealed class LogOnlyHandler(HookEvent hookEvent) : IHookHandler
{
    public HookEvent Event => hookEvent;

    public HookFailurePolicy OnEngineError(HookFailureState state) => HookFailurePolicy.Allow;

    public bool Claims(HookInput input) => true;

    public Task<HookReply> DecideAsync(HookInvocation invocation, CancellationToken cancellationToken) => Task.FromResult(HookReply.Of(HookDecision.Allow));
}
