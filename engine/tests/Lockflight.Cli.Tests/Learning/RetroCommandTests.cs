using System.Globalization;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Tests.Learning;

public sealed class RetroCommandTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-retro-");

    [Fact]
    public async Task TheActivityShowsWhatTheStopHookGateCostAndCountsApprovals()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        string ledger = Path.Combine(root.FullName, ".lockflight", "ledger");
        Directory.CreateDirectory(ledger);
        string at = DateTimeOffset.UtcNow.AddHours(-1).ToString("O", CultureInfo.InvariantCulture);
        string[] lines =
        [
            $$"""{"ts":"{{at}}","type":"gate.completed","signature":"gate:stop:PASS:","summary":"PASS","trigger":"stop-hook","durationMs":61500,"cache":"miss"}""",
            $$"""{"ts":"{{at}}","type":"gate.completed","signature":"gate:stop:PASS:","summary":"PASS","trigger":"stop-hook","durationMs":500,"cache":"hit"}""",
            $$"""{"ts":"{{at}}","type":"gate.completed","signature":"gate:stop-unit:services/orders","summary":"61000","detail":"PASS","trigger":"stop-hook","durationMs":61000,"cache":"miss"}""",
            $$"""{"ts":"{{at}}","type":"gate.completed","signature":"gate:stop:FAIL:build","summary":"FAIL","trigger":"command","durationMs":3000,"cache":"none"}""",
            $$"""{"ts":"{{at}}","type":"gate.completed","signature":"gate:pr:PASS:","summary":"PASS"}""",
            $$"""{"ts":"{{at}}","type":"approval.recorded","signature":"approve:spec","summary":"f1 spec approved","feature":"f1","phase":"spec","signed":false}""",
        ];
        File.WriteAllLines(Path.Combine(ledger, $"events-{DateTimeOffset.UtcNow:yyyy-MM}.jsonl"), lines);

        (_, string output, _) = await TestConsole.RunAsync(["ledger", "retro"], string.Empty, root.FullName);

        Assert.Contains("- Gate runs: 4 (3 PASS)", output, StringComparison.Ordinal);
        Assert.Contains("- Stop hook gates: 2 (1 from the cache), 62.0 s in all", output, StringComparison.Ordinal);
        Assert.Contains("- Approvals: 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheActivityCountsReviewsOfRecordAndHowFindingsClosed()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        string ledger = Path.Combine(root.FullName, ".lockflight", "ledger");
        Directory.CreateDirectory(ledger);
        string at = DateTimeOffset.UtcNow.AddHours(-1).ToString("O", CultureInfo.InvariantCulture);
        string[] lines =
        [
            $$"""{"ts":"{{at}}","type":"evidence.recorded","signature":"review:clean","summary":"f1: 0 findings, 0 confirmed and 0 unverified blocker/major, $0.500"}""",
            $$"""{"ts":"{{at}}","type":"review.completed","signature":"review:unverified","summary":"f1: run 1, of record, 1 open, $1.250","ofRecord":true}""",
            $$"""{"ts":"{{at}}","type":"review.completed","signature":"review:not-of-record","summary":"f1: run 2, not of record, $0.250","ofRecord":false}""",
            $$"""{"ts":"{{at}}","type":"finding.fixed","signature":"finding:fixed","summary":"f1 0123456789ab fixed"}""",
            $$"""{"ts":"{{at}}","type":"finding.dismissed","signature":"finding:dismissed","summary":"f1 abcdefabcdef dismissed"}""",
        ];
        File.WriteAllLines(Path.Combine(ledger, $"events-{DateTimeOffset.UtcNow:yyyy-MM}.jsonl"), lines);

        (_, string output, _) = await TestConsole.RunAsync(["ledger", "retro"], string.Empty, root.FullName);

        Assert.Contains("- Reviews: 3 (1 of record, $2.00) · findings fixed: 1, dismissed: 1", output, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
