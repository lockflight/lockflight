using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class CiTriggerDefaultTests
{
    [Theory]
    [InlineData("0.2.0-alpha.4.dev.20261027120000")]
    [InlineData("0.2.1-0.dev.20261027120000")]
    public void ADevelopmentBuildRunsCiOnlyWhenStartedByHand(string engineVersion) =>
        Assert.Equal(CiTrigger.Manual, CiTriggerDefault.For(engineVersion));

    [Theory]
    [InlineData("0.2.0-alpha.5")]
    [InlineData("0.2.0")]
    public void APublishedVersionRunsCiOnEveryPullRequest(string engineVersion) =>
        Assert.Equal(CiTrigger.PullRequest, CiTriggerDefault.For(engineVersion));
}
