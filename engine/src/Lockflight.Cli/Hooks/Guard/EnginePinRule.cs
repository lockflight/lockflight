using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Hooks.Guard;

/// <summary>
/// Gate parity: a stale linked binary must not judge the project with old rules. Like recovery mode in the shim, reading and the
/// exact restore/link command stay allowed so the agent can repair it; a tool manifest that cannot be read blocks the same way.
/// A lockdown replaces this check: under it only reading passes, which this rule would allow anyway.
/// </summary>
internal sealed class EnginePinRule : IGuardRule
{
    public const string RuleId = "engine-pin";

    public string Id => RuleId;

    public ValueTask<GuardBlock?> CheckAsync(GuardFacts facts, CancellationToken cancellationToken) =>
        ValueTask.FromResult(LockdownPolicy.IsActive(facts.Manifest.Root) ? null : PinMismatch(facts.Input, facts.Manifest));

    private static GuardBlock? PinMismatch(HookInput input, Manifest manifest)
    {
        ToolPinReading pin = ToolPin.ReadPin(manifest.Root);
        bool mismatch = pin.Status == ToolPinStatus.Unreadable
            || (pin.Status == ToolPinStatus.Pinned && !ToolPin.Matches(pin.Version!, ToolPin.RunningVersion));
        if (!mismatch || ReadOnlyTools.Allows(input) || RecoveryCommands.Allows(input))
        {
            return null;
        }

        string reason = pin.Status == ToolPinStatus.Unreadable
            ? $"lockflight: the engine pin cannot be read: {pin.Problem}. Fix dotnet-tools.json (a protected file: a human edit), then run exactly: {RecoveryCommands.RestoreAndLink}"
            : EngineVersion.IsDevelopmentBuild(pin.Version!)
                ? $"lockflight: running engine {ToolPin.RunningVersion} but the project pins the development build {pin.Version}, which no package feed has. Ask the user to {HumanOnlyCommands.OpenATerminalAndRun($"{EngineCommand.UserInstall} update")} in the project."
                : $"lockflight: running engine {ToolPin.RunningVersion} but the project pins {pin.Version}. Run exactly: {RecoveryCommands.RestoreAndLink}";
        return new GuardBlock("stale-engine", reason);
    }
}
