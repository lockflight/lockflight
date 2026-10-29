using Lockflight.Cli.Hooks.Guard;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Hooks.Events;

/// <summary>PreToolUse: the guard (<see cref="GuardRules"/>). Fails closed: an engine error blocks the tool call.</summary>
internal sealed class PreToolUseHandler : IHookHandler
{
    public HookEvent Event => HookEvent.PreToolUse;

    public HookFailurePolicy OnEngineError(HookFailureState state) => HookFailurePolicy.Block;

    public bool Claims(HookRequest input) => true;

    public async Task<HookReply> DecideAsync(HookInvocation invocation, CancellationToken cancellationToken) =>
        HookReply.Of(await GuardRules.DecideAsync(invocation.Input, invocation.Manifest, invocation.Context, new DirectoryReads(), cancellationToken));
}
