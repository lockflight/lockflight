using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Hooks.Guard;

/// <summary>A shell command that runs a command reserved for a person (<see cref="HumanOnlyCommands"/>) is blocked.</summary>
internal sealed class HumanOnlyCommandRule : IGuardRule
{
    public const string RuleId = "human-only-command";

    public string Id => RuleId;

    public ValueTask<GuardBlock?> CheckAsync(GuardFacts facts, CancellationToken cancellationToken) =>
        ValueTask.FromResult(facts.Input.Command is string command && HumanOnlyCommands.FindIn(command, facts.Dialect) is string blocked
            ? new GuardBlock("human-only-command", $"lockflight: 'lockflight {blocked}' records a human decision; {HumanOnlyCommands.AskTheUser(blocked)}.")
            : null);
}
