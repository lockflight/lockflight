using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests;

public sealed class GitConfigurationTests
{
    [Fact]
    public async Task GitStartedByATestSeesNoGlobalConfiguration()
    {
        ProcessResult global = await ProcessRunner.RunAsync("git", ["config", "--global", "--list"], Path.GetTempPath(), TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, global.Output.Trim());
    }
}
