using Lockflight.Cli.Hooks.Requests;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>The engine side of one in-session human command, created for one hook invocation by its <see cref="SessionCommandSpec"/>.</summary>
internal interface ISessionDecider
{
    Task<SessionCommandResult> DecideAsync(HookRequest input, CancellationToken cancellationToken);
}
