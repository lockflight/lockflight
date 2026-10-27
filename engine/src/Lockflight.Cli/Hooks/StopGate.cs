using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Stop hook: runs the stop profile when the manifest enables it, skipping the run when nothing changed since the last PASS.
/// Every decision it makes is recorded as a <c>gate.completed</c> line with the <c>stop-hook</c> trigger, its time and whether
/// the cache answered, so the Stop gate's cost can be measured. Best effort — CI is the hard definition of done.
/// </summary>
internal static class StopGate
{
    /// <param name="sessionId">The Claude Code session, recorded with a block so the TaskCompleted hook does not block on it again.</param>
    public static async Task<HookDecision> DecideAsync(Manifest manifest, string? sessionId, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!manifest.StopGate)
        {
            return HookDecision.Allow;
        }

        long started = time.GetTimestamp();
        StopGateCache cache = new(manifest.Root, unitPath: null);
        string? inputsHash = await InputsHash.ComputeAsync(manifest.Root, cancellationToken);
        StopCacheResult cacheResult = inputsHash is null ? StopCacheResult.None : StopCacheResult.Miss;
        if (inputsHash is not null && await cache.PassedWithAsync(inputsHash, cancellationToken))
        {
            GateResult cached = new(manifest.Root, GateProfile.Stop, Verdict.Pass, TestsTotal: null, Steps: []);
            await RecordAsync(manifest, cached, Run(time, started, StopCacheResult.Hit), time, cancellationToken);
            new StopBlockRecord(manifest.Root).Clear();
            return HookDecision.Allow;
        }

        GateResult result;
        if (manifest.Units.Count > 0 && Path.GetFullPath(manifest.StopTarget ?? ".", manifest.Root).TrimEnd(Path.DirectorySeparatorChar) == Path.GetFullPath(manifest.Root).TrimEnd(Path.DirectorySeparatorChar))
        {
            // A workspace: every changed unit, each also with its own cache entry, so a PASS of one survives edits to another.
            GateRunContext workspace = new(manifest.Root, manifest.Root, GateProfile.Stop, manifest.Gates, BaseRef: null, time);
            result = await WorkspaceGateRunner.RunAsync(manifest, workspace, [], useCache: true, TextWriter.Null, cancellationToken);
            await StopTimings.RecordAsync(manifest, result, time, cancellationToken);
        }
        else
        {
            string target = Path.GetFullPath(manifest.StopTarget ?? ".", manifest.Root);
            // A stop target inside a unit runs with that unit's build: file, test framework and stop filter.
            GateRunContext run = GateRunContext.ForTarget(manifest, target, GateProfile.Stop, baseRef: null, time);
            result = await GateRunner.RunAsync(run, TextWriter.Null, cancellationToken);
        }

        await RecordAsync(manifest, result, Run(time, started, cacheResult), time, cancellationToken);
        StopBlockRecord blocks = new(manifest.Root);
        if (!VerdictNames.AllowsNextStep(result.Verdict))
        {
            await blocks.RememberAsync(sessionId, inputsHash, cancellationToken);
            return Blocked(result);
        }

        blocks.Clear();
        if (inputsHash is not null)
        {
            await cache.RememberPassAsync(inputsHash, cancellationToken);
        }

        return HookDecision.Allow;
    }

    private static LedgerGateRun Run(TimeProvider time, long started, StopCacheResult cache) =>
        new(GateTrigger.StopHook, (long)time.GetElapsedTime(started).TotalMilliseconds, cache);

    private static Task RecordAsync(Manifest manifest, GateResult result, LedgerGateRun run, TimeProvider time, CancellationToken cancellationToken) =>
        new LedgerWriter(manifest.Root, time).AppendAsync(GateRunEvents.Completed(result, run), cancellationToken);

    private static HookDecision Blocked(GateResult result) => HookDecision.Block(
        $"stop-gate:{VerdictNames.ToContractName(result.Verdict)}",
        $"lockflight stop gate is {VerdictNames.ToContractName(result.Verdict)} ({result.Summary}). Fix it before finishing.");
}
