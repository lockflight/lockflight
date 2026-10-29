using Lockflight.Cli.Canaries;
using Lockflight.Cli.Hooks;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary>
/// The engine's canary suite beyond the registered controls (<see cref="CanaryCoverageTests"/>): a violation planted in front of the
/// in-session commands, the test gate's judgement, the TaskCompleted hook, and, on a generated .NET project, the real test gate, a
/// real red, an amend, a test file edited after red and a trace with a project that does not evaluate. Each canary has a harmless
/// twin that must pass; a canary whose twin is refused too is BROKEN, which fails as a miss does.
/// </summary>
[Trait(CanarySuite.Trait, CanarySuite.TraitValue)]
public sealed class CanaryTests
{
    [Fact]
    public async Task InSessionCommandsRefuseEveryPlantedViolation()
    {
        using CanaryScratch scratch = new(TimeProvider.System);

        CanarySuite.AssertAllCaught(await SessionCommandCanaries.RunAsync(scratch, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheTestGateDoesNotPassAFailingASkippedOrAnEmptyRunOnEitherPlatform()
    {
        using CanaryScratch scratch = new(TimeProvider.System);

        IReadOnlyList<CanaryReport> reports = TestGateCanaries.Run(scratch);

        CanarySuite.AssertAllCaught(reports);
        Assert.Contains(reports, report => report.Name == "test gate (VSTest): every test skipped is not a PASS");
    }

    [Fact]
    public async Task ATaskCompletedWithoutAStopGatePassForTheTreeIsRefused() =>
        CanarySuite.AssertAllCaught([await TaskCompletedCanaries.RunAsync(TimeProvider.System, TestContext.Current.CancellationToken)]);

    [Fact]
    public async Task TheRealGatesAndRedGreenOnAGeneratedProjectCatchEveryPlantedViolation()
    {
        IReadOnlyList<CanaryReport> reports = await FullCanaries.RunAsync(TimeProvider.System, TestContext.Current.CancellationToken);

        CanarySuite.AssertAllCaught(reports);
        Assert.Contains(reports, report => report.Name == "full: a real red failing by Assert.Fail is rejected");
        Assert.Contains(reports, report => report.Name == "full: a project whose evaluation fails does not let trace PASS");
    }

    [Fact]
    public async Task AGuardThatBlocksHarmlessWorkTooIsBrokenNotCaught()
    {
        using CanaryScratch scratch = new(TimeProvider.System);
        string marker = scratch.PathOf(LockdownPolicy.RelativeMarkerPath);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, "lockdown\n");

        IReadOnlyList<CanaryReport> reports = await GuardCanaries.RunAsync(scratch, TestContext.Current.CancellationToken);

        CanaryReport report = Assert.Single(reports);
        Assert.Equal(CanaryResult.Broken, report.Result);
        Assert.Contains("blocks a harmless call too", report.Detail, StringComparison.Ordinal);
    }
}
