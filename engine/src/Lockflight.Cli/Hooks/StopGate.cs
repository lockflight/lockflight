using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Stop hook: runs the stop profile when the manifest enables it, skipping the run when nothing changed since the last PASS.
/// Best effort — CI is the hard definition of done.
/// </summary>
internal static class StopGate
{
    public static async Task<HookDecision> DecideAsync(Manifest manifest, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!manifest.StopGate)
        {
            return HookDecision.Allow;
        }

        StopGateCache cache = new(manifest.Root, unitPath: null);
        string? inputsHash = await InputsHash.ComputeAsync(manifest.Root, cancellationToken);
        if (inputsHash is not null && await cache.PassedWithAsync(inputsHash, cancellationToken))
        {
            return HookDecision.Allow;
        }

        if (manifest.Units.Count > 0 && Path.GetFullPath(manifest.StopTarget ?? ".", manifest.Root).TrimEnd(Path.DirectorySeparatorChar) == Path.GetFullPath(manifest.Root).TrimEnd(Path.DirectorySeparatorChar))
        {
            // A workspace: every changed unit, each also with its own cache entry, so a PASS of one survives edits to another.
            GateRunContext workspace = new(manifest.Root, manifest.Root, GateProfile.Stop, manifest.Gates, BaseRef: null, time);
            GateResult judged = await WorkspaceGateRunner.RunAsync(manifest, workspace, [], useCache: true, TextWriter.Null, cancellationToken);
            await StopTimings.RecordAsync(manifest, judged, time, cancellationToken);
            if (!VerdictNames.AllowsNextStep(judged.Verdict))
            {
                return Blocked(judged);
            }

            if (inputsHash is not null)
            {
                await cache.RememberPassAsync(inputsHash, cancellationToken);
            }

            return HookDecision.Allow;
        }

        string target = Path.GetFullPath(manifest.StopTarget ?? ".", manifest.Root);

        GateRunContext run = new(target, manifest.Root, GateProfile.Stop, manifest.Gates, BaseRef: null, time) { UnitPath = UnitResolver.Owning(manifest, target)?.Path, BuildFile = manifest.Gates.BuildFile };
        GateResult result = await GateRunner.RunAsync(run, TextWriter.Null, cancellationToken);
        if (VerdictNames.AllowsNextStep(result.Verdict))
        {
            if (inputsHash is not null)
            {
                await cache.RememberPassAsync(inputsHash, cancellationToken);
            }

            return HookDecision.Allow;
        }

        return Blocked(result);
    }

    private static HookDecision Blocked(GateResult result) => HookDecision.Block(
        $"stop-gate:{VerdictNames.ToContractName(result.Verdict)}",
        $"lockflight stop gate is {VerdictNames.ToContractName(result.Verdict)} ({result.Summary}). Fix it before finishing.");
}
