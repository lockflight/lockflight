namespace Lockflight.Cli.Hooks.Guard;

/// <summary>While the project is in lockdown (<see cref="LockdownPolicy"/>), every call but reading is blocked.</summary>
internal sealed class LockdownRule : IGuardRule
{
    public const string RuleId = "lockdown";

    public string Id => RuleId;

    public ValueTask<GuardBlock?> CheckAsync(GuardFacts facts, CancellationToken cancellationToken) =>
        ValueTask.FromResult(LockdownPolicy.IsActive(facts.Manifest.Root) ? GuardBlock.Of(LockdownPolicy.Decide(facts.Input)) : null);
}
