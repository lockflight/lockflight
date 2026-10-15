using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class TestAppsTests
{
    [Theory]
    [InlineData("net8.0;net10.0", null, "net10.0")]
    [InlineData("net8.0;net10.0", "net8.0", "net8.0")]
    [InlineData("net8.0;net10.0", "net9.0", "net10.0")]
    [InlineData("net472;net8.0", null, "net8.0")]
    [InlineData("netstandard2.0", null, null)]
    public void MultiTargetedTestProjectsRunOneFramework(string frameworks, string? preferred, string? expected) =>
        Assert.Equal(expected, TestApps.ChooseFramework(frameworks, preferred));
}
