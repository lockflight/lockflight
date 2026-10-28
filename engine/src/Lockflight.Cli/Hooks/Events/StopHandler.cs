namespace Lockflight.Cli.Hooks.Events;

/// <summary>Stop: the stop gate (<see cref="StopGate"/>). An engine error is reported and never bricks the session.</summary>
internal sealed class StopHandler : IHookHandler
{
    public HookEvent Event => HookEvent.Stop;

    public HookFailurePolicy OnEngineError(HookFailureState state) => HookFailurePolicy.Allow;

    public bool Claims(HookInput input) => true;

    public async Task<HookReply> DecideAsync(HookInvocation invocation, CancellationToken cancellationToken) =>
        HookReply.Of(await StopGate.DecideAsync(invocation.Manifest, invocation.Input.SessionId, invocation.Context.Time, cancellationToken));
}
