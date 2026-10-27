using Lockflight.Cli.Learning;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Tests.Learning;

public sealed class IncidentClusteringTests
{
    [Fact]
    public void IncidentsClusterByFingerprintAndNormalWorkIsIgnored()
    {
        LedgerRecord[] records =
        [
            Record("gate.completed", "gate:stop:FAIL:format", "FAIL"),
            Record("gate.completed", "gate:stop:FAIL:format", "FAIL"),
            Record("gate.completed", "gate:stop:PASS:", "PASS"),
            Record("hook.blocked", "pre-tool-use:protected-path:global.json", "protected-path:global.json"),
            Record("evidence.recorded", "redgreen:red:rejected", "f1: RED_IN_TEST"),
            Record("evidence.recorded", "redgreen:red:accepted", "f1: cycle 1"),
            Record("evidence.recorded", "redgreen:green:rejected", "f1: cycle 1"),
            Record("config.written", "approve:spec", "f1 approved"),
        ];

        IReadOnlyList<IncidentCluster> clusters = IncidentClustering.Cluster(records);

        Assert.Equal(["gate:stop:FAIL:format", "pre-tool-use:protected-path:global.json", "redgreen:red:rejected:RED_IN_TEST"], clusters.Select(cluster => cluster.Fingerprint));
        Assert.Equal(2, clusters[0].Count);
        Assert.Equal(RemedyRung.Analyzer, clusters[0].Rung);
        Assert.Equal(IncidentClass.RuleIgnored, clusters[1].Class);
        Assert.Equal(IncidentClass.ProcessSkipped, clusters[2].Class);
    }

    [Fact]
    public void ReviewRunsWithOpenFindingsAreIncidentsAndClosedFindingsAreNot()
    {
        LedgerRecord[] records =
        [
            Record("review.completed", "review:changes", "f1: run 1"),
            Record("review.completed", "review:unverified", "f1: run 2"),
            Record("review.completed", "review:clean", "f1: run 3"),
            Record("review.completed", "review:not-of-record", "f1: run 4"),
            Record("finding.fixed", "finding:fixed", "f1 0123456789ab fixed"),
            Record("finding.dismissed", "finding:dismissed", "f1 0123456789ab dismissed"),
        ];

        Assert.Equal(["review:changes", "review:unverified"], IncidentClustering.Cluster(records).Select(cluster => cluster.Fingerprint));
    }

    [Fact]
    public void StopUnitTimingsAndStopHookGateRunsAreNotIncidents()
    {
        LedgerRecord[] records =
        [
            Record("gate.completed", "gate:stop-unit:services/orders", "184000"),
            Record("gate.completed", "gate:stop:FAIL:build", "FAIL") with { Trigger = "stop-hook" },
            Record("gate.completed", "gate:stop:FAIL:build", "FAIL") with { Trigger = "command" },
            Record("approval.recorded", "approve:spec", "f1 spec approved"),
        ];

        IncidentCluster cluster = Assert.Single(IncidentClustering.Cluster(records));

        Assert.Equal("gate:stop:FAIL:build", cluster.Fingerprint);
        Assert.Equal(1, cluster.Count);
    }

    [Fact]
    public void MalformedLedgerLinesAreSkipped()
    {
        Assert.Null(LedgerReader.Parse("{ not json"));
        Assert.Null(LedgerReader.Parse("""{"type":"hook.blocked"}"""));
        Assert.NotNull(LedgerReader.Parse("""{"ts":"2026-10-11T12:00:00+00:00","type":"hook.blocked","signature":"s","summary":"x"}"""));
    }

    private static LedgerRecord Record(string type, string signature, string summary) => new(DateTimeOffset.UnixEpoch, type, signature, summary, null);
}
