using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Hooks.Guard;

/// <summary>A call that would queue an in-session human command as a prompt is blocked (<see cref="SessionCommandRelay"/>).</summary>
internal sealed class SessionRelayRule(EnvironmentReader environment) : IGuardRule
{
    public const string RuleId = "session-relay";

    public string Id => RuleId;

    public ValueTask<GuardBlock?> CheckAsync(GuardFacts facts, CancellationToken cancellationToken) =>
        ValueTask.FromResult(SessionCommandRelay.Decide(facts.Input, environment.HomeDirectory()) is HookDecision decision ? GuardBlock.Of(decision) : null);
}
