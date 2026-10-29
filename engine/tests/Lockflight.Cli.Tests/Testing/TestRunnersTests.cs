using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class TestRunnersTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(7, 7)]
    public void AnAppRunsOnTheRunnerOfItsMode(int mode, int runner) => Assert.Equal((RunnerMode)runner, TestRunners.For((RunnerMode)mode, TimeProvider.System).Mode);

    [Fact]
    public void EveryModeButUnknownHasOneRunner() =>
        Assert.Equal(
            Enum.GetValues<RunnerMode>().Where(mode => mode != RunnerMode.Unknown).Order(),
            TestRunners.All(TimeProvider.System).Select(runner => runner.Mode).Order());

    [Theory]
    [InlineData(5, "Microsoft.Testing.Extensions.TrxReport")]
    [InlineData(4, "Microsoft.NET.Test.Sdk")]
    [InlineData(7, "Microsoft.Testing.Extensions.TrxReport")]
    [InlineData(6, "Microsoft.NET.Test.Sdk")]
    public async Task AnNUnitOrMSTestAppWithoutWhatItsPlatformNeedsSaysWhatToAdd(int mode, string package)
    {
        DirectoryInfo output = Directory.CreateTempSubdirectory("lockflight-runners-");
        try
        {
            TestApp app = new(output.FullName, Path.Combine(output.FullName, "Shop.Tests.dll"), (RunnerMode)mode);

            TestListing listing = await TestRunners.ListAsync(app, output.FullName, TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);

            Assert.False(listing.Listed);
            Assert.Contains(package, listing.Problem, StringComparison.Ordinal);
        }
        finally
        {
            output.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AnNUnitAppOnTheTestingPlatformWithoutItsTrxReportFallsBackToVSTest()
    {
        DirectoryInfo output = Directory.CreateTempSubdirectory("lockflight-runners-");
        try
        {
            string assembly = Path.Combine(output.FullName, "Shop.Tests.dll");
            File.Copy(typeof(TestRunnersTests).Assembly.Location, assembly);
            await File.WriteAllTextAsync(Path.Combine(output.FullName, "testhost.dll"), string.Empty, TestContext.Current.CancellationToken);

            TestListing listing = await TestRunners.ListAsync(
                new TestApp(output.FullName, assembly, RunnerMode.NUnitMtp), output.FullName, TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);

            Assert.True(listing.Listed);
            Assert.Equal(RunnerMode.NUnit, listing.Mode);
            Assert.Contains(listing.Tests, test => test.Method == "Plain");
        }
        finally
        {
            output.Delete(recursive: true);
        }
    }

    [Fact]
    public void OnlyXunitModesWriteReviewReproductions() =>
        Assert.Equal(
            [RunnerMode.Unknown, RunnerMode.Native, RunnerMode.Mtp, RunnerMode.VSTest],
            Enum.GetValues<RunnerMode>().Where(TestRunners.IsXunit).Order());

    [Fact]
    public async Task AnAssemblyThatCannotBeReadIsNotListedByVSTest()
    {
        TestApp app = new(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), $"lockflight-missing-{Guid.CreateVersion7()}.dll")) { Mode = RunnerMode.VSTest };

        TestListing listing = await TestRunners.ListAsync(app, Path.GetTempPath(), TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.False(listing.Listed);
    }
}
