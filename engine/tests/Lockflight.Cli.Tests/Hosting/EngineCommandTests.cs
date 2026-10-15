using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hosting;

public sealed class EngineCommandTests
{
    [Theory]
    [InlineData("/home/u/.lockflight/tools/0.2.0-dev.1/lockflight", EngineCommand.UserInstall)]
    [InlineData("/home/u/.lockflight/bin/lockflight", EngineCommand.UserInstall)]
    [InlineData("/home/u/project/.lockflight/bin/lockflight", EngineCommand.Tool)]
    [InlineData("/home/u/.nuget/packages/lockflight.cli/0.2.0/tools/lockflight", EngineCommand.Tool)]
    public void TheCommandFollowsWhereTheEngineRunsFrom(string processPath, string expected)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(expected, EngineCommand.For(processPath, "/home/u"));
    }

    [Fact]
    public void AnUnknownProcessPathIsTheToolPin() => Assert.Equal(EngineCommand.Tool, EngineCommand.For(null, "/home/u"));
}
