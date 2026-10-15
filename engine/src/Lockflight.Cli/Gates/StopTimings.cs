using System.Globalization;
using Lockflight.Cli.Config;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Gates;

/// <summary>
/// How long each unit's Stop gate run takes, recorded in the ledger (<c>gate:stop-unit:&lt;unit&gt;</c>, summary in milliseconds)
/// and read back for <c>doctor</c>: a unit whose build and tests take minutes at every Stop is better served by a
/// <c>test.stopFilter</c>. The ledger is local and fail-open, so this is advice, never a check. These lines sit beside the
/// whole run's <c>gate:stop:…</c> line and are neither verdicts nor incidents (<see cref="IsTiming"/>).
/// </summary>
internal static class StopTimings
{
    public const string SignaturePrefix = "gate:stop-unit:";

    /// <summary>A unit whose 95th-percentile Stop time is above this gets the advice.</summary>
    public static readonly TimeSpan SlowThreshold = TimeSpan.FromMinutes(2);

    /// <summary>Whether a ledger signature is a unit's Stop time rather than a gate run's verdict.</summary>
    public static bool IsTiming(string signature) => signature.StartsWith(SignaturePrefix, StringComparison.Ordinal);

    public static async Task RecordAsync(Manifest manifest, GateResult result, TimeProvider time, CancellationToken cancellationToken)
    {
        LedgerWriter ledger = new(manifest.Root, time);
        foreach (Unit unit in manifest.Units)
        {
            string prefix = unit.Path + ": ";
            GateStep[] steps = [.. result.Steps.Where(step => step.Name.StartsWith(prefix, StringComparison.Ordinal))];
            if (steps.Length > 0)
            {
                long milliseconds = steps.Sum(step => step.DurationMs);
                LedgerEvent timing = new(LedgerEventType.GateCompleted, SignaturePrefix + unit.Path,
                    milliseconds.ToString(CultureInfo.InvariantCulture), VerdictNames.ToContractName(result.Verdict))
                {
                    GateRun = new LedgerGateRun(GateTrigger.StopHook, milliseconds, StopCacheResult.Miss),
                };
                await ledger.AppendAsync(timing, cancellationToken);
            }
        }
    }

    /// <summary>Units whose recorded Stop times have a 95th percentile above <see cref="SlowThreshold"/>, slowest first.</summary>
    public static IReadOnlyList<(string Unit, TimeSpan P95)> Slow(string root, DateTimeOffset since) =>
        [.. LedgerReader.Read(root, since)
            .Where(record => IsTiming(record.Signature))
            .Select(record => (Unit: record.Signature[SignaturePrefix.Length..], Ms: long.TryParse(record.Summary, NumberStyles.None, CultureInfo.InvariantCulture, out long ms) ? ms : -1))
            .Where(entry => entry.Ms >= 0)
            .GroupBy(entry => entry.Unit, StringComparer.Ordinal)
            .Select(group => (Unit: group.Key, P95: TimeSpan.FromMilliseconds(Percentile95([.. group.Select(entry => entry.Ms)]))))
            .Where(entry => entry.P95 > SlowThreshold)
            .OrderByDescending(entry => entry.P95)];

    private static long Percentile95(long[] values)
    {
        Array.Sort(values);
        return values[(int)Math.Ceiling(values.Length * 0.95) - 1];
    }
}
