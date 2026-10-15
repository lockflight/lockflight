using System.Globalization;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Learning;

/// <summary>
/// <c>lockflight ledger retro [--days 30] [--min 2]</c> — the Tier-0 retro: clusters the ledger's incidents by
/// fingerprint, pre-classifies them and suggests a remedy rung. A report for a person; remedies are human-authored PRs.
/// </summary>
internal sealed class RetroCommand(CliContext context)
{
    private const int DefaultDays = 30;
    private const int DefaultMinimum = 2;

    private static readonly string[] ValueOptions = ["--days", "--min"];

    public async Task<ExitCode> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync("ledger retro", args, ValueOptions, [], context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        if (PositiveOrDefault(options.Value("--days"), DefaultDays) is not int days || PositiveOrDefault(options.Value("--min"), DefaultMinimum) is not int minimum)
        {
            await context.Error.WriteLineAsync("lockflight: --days and --min take a whole number of at least 1");
            return ExitCode.Config;
        }

        if (await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        DateTimeOffset now = context.Time.GetUtcNow();
        IReadOnlyList<LedgerRecord> records = LedgerReader.Read(manifest.Root, now.AddDays(-days));
        IReadOnlyList<IncidentCluster> clusters = IncidentClustering.Cluster(records);
        IncidentCluster[] recurring = [.. clusters.Where(cluster => cluster.Count >= minimum)];

        TextWriter output = context.Output;
        await output.WriteLineAsync($"# Lockflight retro — last {days} days\n");
        await output.WriteLineAsync($"{records.Count} ledger events · {clusters.Sum(cluster => cluster.Count)} incidents in {clusters.Count} fingerprints · {recurring.Length} recurring (≥ {minimum})\n");
        await WriteActivityAsync(records);

        if (recurring.Length == 0)
        {
            await output.WriteLineAsync("No recurring incidents. Nothing to remedy yet.");
            return ExitCode.Pass;
        }

        await output.WriteLineAsync("## Recurring incidents\n");
        await output.WriteLineAsync("| # | Fingerprint | Count | Last seen | Class | Suggested rung |");
        await output.WriteLineAsync("|---|---|---|---|---|---|");
        for (int index = 0; index < recurring.Length; index++)
        {
            IncidentCluster cluster = recurring[index];
            await output.WriteLineAsync($"| {index + 1} | `{cluster.Fingerprint}` | {cluster.Count} | {cluster.LastSeen:yyyy-MM-dd} | {LearningNames.ToName(cluster.Class)} | {LearningNames.ToName(cluster.Rung)} |");
        }

        await output.WriteLineAsync("\n## Suggestions\n");
        for (int index = 0; index < recurring.Length; index++)
        {
            IncidentCluster cluster = recurring[index];
            await output.WriteLineAsync($"{index + 1}. {cluster.Suggestion}. Latest: \"{cluster.Example}\"");
        }

        await output.WriteLineAsync("\nRemedies are changes a person reviews: prefer the most deterministic rung; gates, hooks and always-on rules always need a human.");
        return ExitCode.Pass;
    }

    /// <summary>The fallback when the option is absent, the number when it is a positive whole number, otherwise null.</summary>
    private static int? PositiveOrDefault(string? value, int fallback) =>
        value is null ? fallback : int.TryParse(value, CultureInfo.InvariantCulture, out int parsed) && parsed > 0 ? parsed : null;

    private async Task WriteActivityAsync(IReadOnlyList<LedgerRecord> records)
    {
        LedgerRecord[] gateRuns = [.. records.Where(record => record.Type == LedgerEventTypeNames.GateCompleted && !StopTimings.IsTiming(record.Signature))];
        int passing = gateRuns.Count(record => record.Signature.Contains(LedgerSignatures.GatePassed, StringComparison.Ordinal));
        LedgerRecord[] stopHookRuns = [.. gateRuns.Where(record => record.Trigger == GateTriggerNames.StopHook)];
        int reds = records.Count(record => record.Signature.StartsWith(LedgerSignatures.RedPrefix, StringComparison.Ordinal));
        int acceptedReds = records.Count(record => record.Signature == LedgerSignatures.RedAccepted);
        int reviews = records.Count(record => record.Signature.StartsWith(LedgerSignatures.ReviewPrefix, StringComparison.Ordinal));
        decimal reviewCost = records
            .Where(record => record.Signature.StartsWith(LedgerSignatures.ReviewPrefix, StringComparison.Ordinal))
            .Select(record => record.Summary.LastIndexOf('$') is int dollar and >= 0
                && decimal.TryParse(record.Summary[(dollar + 1)..], CultureInfo.InvariantCulture, out decimal cost) ? cost : 0m)
            .Sum();

        await context.Output.WriteLineAsync("## Activity\n");
        await context.Output.WriteLineAsync($"- Gate runs: {gateRuns.Length} ({passing} PASS)");
        if (stopHookRuns.Length > 0)
        {
            int cacheHits = stopHookRuns.Count(record => record.Cache == StopCacheResultNames.Hit);
            double seconds = stopHookRuns.Sum(record => record.DurationMs ?? 0) / 1000.0;
            await context.Output.WriteLineAsync(
                string.Create(CultureInfo.InvariantCulture, $"- Stop hook gates: {stopHookRuns.Length} ({cacheHits} from the cache), {seconds:0.0} s in all"));
        }

        await context.Output.WriteLineAsync($"- Hook blocks: {records.Count(record => record.Type == LedgerEventTypeNames.HookBlocked)}");
        await context.Output.WriteLineAsync($"- Reds: {reds} ({acceptedReds} accepted) · greens closed: {records.Count(record => record.Signature == LedgerSignatures.GreenAccepted)}");
        await context.Output.WriteLineAsync($"- Approvals: {records.Count(record => record.Signature.StartsWith(LedgerSignatures.ApprovePrefix, StringComparison.Ordinal))}");
        await context.Output.WriteLineAsync($"- Reviews of record: {reviews} (${reviewCost.ToString("0.00", CultureInfo.InvariantCulture)})\n");
    }
}
