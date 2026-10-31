using Lockflight.Cli.Config;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Linking;

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

    private static GuardBlock? PinMismatch(HookRequest input, Manifest manifest)
    {
        if (ReadOnlyTools.Allows(input) || RecoveryCommands.Allows(input))
        {
            return null;
        }

        // The engine lock pins by hash; a project without one still pins the version in dotnet-tools.json.
        EngineLockReading lockReading = EngineLockFiles.ReadForClone(manifest.Root);
        if (lockReading.Problem is string lockProblem)
        {
            return new GuardBlock("stale-engine", $"lockflight: the engine lock cannot be read: {lockProblem}. Fix it (a protected file: a human edit, or `{EngineCommand.UserInstall} update`).");
        }

        if (lockReading.Lock is EngineLock engineLock)
        {
            string? locked = EngineRids.Current is string rid ? engineLock.HashFor(rid) : null;
            return locked is not null && EngineRecord.RecordedHash(manifest.Root) == locked
                ? null
                : new GuardBlock(
                    "stale-engine",
                    $"lockflight: the linked engine ({ToolPin.RunningVersion}) is not the one {EngineLock.RelativePath} pins ({engineLock.Version}). Run exactly: {RecoveryCommands.LinkInstalled} "
                    + $"(it links that engine when it is installed); otherwise ask the user to {HumanOnlyCommands.OpenATerminalAndRun($"{EngineCommand.UserInstall} engine install")}"
                    + $" (with a Lockflight 0.2 install, which has neither command, `{EngineCommand.UserInstall} update`).");
        }

        ToolPinReading pin = ToolPin.ReadPin(manifest.Root);
        bool mismatch = pin.Status == ToolPinStatus.Unreadable
            || (pin.Status == ToolPinStatus.Pinned && !ToolPin.Matches(pin.Version!, ToolPin.RunningVersion));
        if (!mismatch)
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
