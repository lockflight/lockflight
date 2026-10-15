using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Tests.Gates;

public sealed class StopTimingsTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-timings-");

    [Fact]
    public async Task AUnitWhoseStopRunsAreSlowIsReportedWithItsPercentile()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Manifest manifest = new(root.FullName, [], StopGate: true, StopTarget: null, GateSettings.Default)
        {
            Units = [new Unit("services/orders", Build: null, UnitKind.Auto, TestFramework: null, StopFilter: null), new Unit("services/billing", Build: null, UnitKind.Auto, TestFramework: null, StopFilter: null)],
        };
        ManualTime time = new(DateTimeOffset.Parse("2026-10-12T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        foreach (long seconds in new long[] { 150, 170, 200 })
        {
            GateResult result = new(root.FullName, GateProfile.Stop, Verdict.Pass, TestsTotal: 2,
            [
                new GateStep("services/orders: build", 0, Verdict.Pass, seconds * 500, string.Empty),
                new GateStep("services/orders: test", 0, Verdict.Pass, seconds * 500, string.Empty),
                new GateStep("services/billing: build", 0, Verdict.Pass, 5_000, string.Empty),
            ]);
            await StopTimings.RecordAsync(manifest, result, time, cancellationToken);
        }

        IReadOnlyList<(string Unit, TimeSpan P95)> slow = StopTimings.Slow(root.FullName, time.GetUtcNow().AddDays(-1));

        (string unit, TimeSpan p95) = Assert.Single(slow);
        Assert.Equal("services/orders", unit);
        Assert.Equal(TimeSpan.FromSeconds(200), p95);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
