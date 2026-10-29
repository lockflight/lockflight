using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hooks.Requests;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// TaskCompleted hook: a task may be marked completed only when the stop gate has passed for the tree as it is now. It reads
/// evidence only and never runs a build: the PASS the Stop gate or a <c>lockflight gate run</c> cached for the current inputs
/// hash (<see cref="InputsHash"/>, a few git queries). When the Stop gate already blocked this session on this same tree, the
/// agent is already continuing to fix it and the Stop gate judges the tree again before the turn ends, so the completion is not
/// blocked a second time. Best effort, like the Stop gate: CI is the definition of done.
/// </summary>
internal static class TaskCompletedGate
{
    public static async Task<HookDecision> DecideAsync(Manifest manifest, HookRequest input, CancellationToken cancellationToken)
    {
        if (!manifest.StopGate)
        {
            return HookDecision.Allow;
        }

        // Outside git nothing is cached, so there is no evidence to read; the Stop gate still runs at the end of the turn.
        if (await InputsHash.ComputeAsync(manifest.Root, cancellationToken) is not string inputsHash)
        {
            return HookDecision.Allow;
        }

        if (await new StopGateCache(manifest.Root, unitPath: null).PassedWithAsync(inputsHash, cancellationToken)
            || await new StopBlockRecord(manifest.Root).BlockedAsync(input.Session.Id, inputsHash, cancellationToken))
        {
            return HookDecision.Allow;
        }

        string task = string.IsNullOrWhiteSpace(input.TaskSubject) ? "this task" : $"task \"{input.TaskSubject}\"";
        return HookDecision.Block(
            "task-completed:not-passed",
            $"lockflight: {task} is not completed: the stop gate has not passed for the current changes. Run `.lockflight/bin/lockflight gate run`, fix what it reports, then mark the task completed again.");
    }
}
