using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class TestRunnersTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void AnAppRunsOnTheRunnerOfItsMode(int mode, int runner) => Assert.Equal((RunnerMode)runner, TestRunners.For((RunnerMode)mode, TimeProvider.System).Mode);

    [Fact]
    public void EveryModeButUnknownHasOneRunner() =>
        Assert.Equal(
            Enum.GetValues<RunnerMode>().Where(mode => mode != RunnerMode.Unknown).Order(),
            TestRunners.All(TimeProvider.System).Select(runner => runner.Mode).Order());

    [Fact]
    public async Task AnAssemblyThatCannotBeReadIsNotListedByVSTest()
    {
        TestApp app = new(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), $"lockflight-missing-{Guid.CreateVersion7()}.dll")) { Mode = RunnerMode.VSTest };

        TestListing listing = await TestRunners.ListAsync(app, Path.GetTempPath(), TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.False(listing.Listed);
    }
}
