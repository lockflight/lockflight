using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Gates;

/// <summary>
/// The <c>gate.completed</c> ledger event of a whole gate run: <c>gate:&lt;profile&gt;:&lt;verdict&gt;:&lt;failing steps&gt;</c>,
/// the verdict as summary, the steps as detail, and how the run came about. Command-line runs and the Stop hook share it, so
/// readers group them by signature and tell them apart by trigger.
/// </summary>
internal static class GateRunEvents
{
    private const string CacheHitDetail = "unchanged since the last PASS; not run";

    public static LedgerEvent Completed(GateResult result, LedgerGateRun run)
    {
        string failingSteps = string.Join(',', result.Steps.Where(step => !VerdictNames.AllowsNextStep(step.Verdict)).Select(step => step.Name));
        string verdict = VerdictNames.ToContractName(result.Verdict);
        return new LedgerEvent(
            LedgerEventType.GateCompleted,
            $"gate:{GateProfileNames.ToName(result.Profile)}:{verdict}:{failingSteps}",
            verdict,
            result.Steps.Count == 0 && run.Cache == StopCacheResult.Hit ? CacheHitDetail : result.Summary)
        {
            GateRun = run,
        };
    }
}
