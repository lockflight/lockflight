namespace Lockflight.Cli.Hooks.Events;

/// <summary>TaskCompleted: a task is done only on a stop-gate PASS for the tree (<see cref="TaskCompletedGate"/>).</summary>
internal sealed class TaskCompletedHandler : IHookHandler
{
    public HookEvent Event => HookEvent.TaskCompleted;

    public HookFailurePolicy OnEngineError(HookFailureState state) => HookFailurePolicy.Allow;

    public bool Claims(HookInput input) => true;

    public async Task<HookReply> DecideAsync(HookInvocation invocation, CancellationToken cancellationToken) =>
        HookReply.Of(await TaskCompletedGate.DecideAsync(invocation.Manifest, invocation.Input, cancellationToken));
}
