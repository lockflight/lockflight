using Lockflight.Cli.Hosting;
using Lockflight.Cli.Tests.Ci;

namespace Lockflight.Cli.Tests.Lifecycle.Fix;

/// <summary>Under a base policy whose class needs a spec from the start (strict), a branch with no spec is refused before any build.</summary>
public sealed class RedCommandClassTests : RedCommandScenario
{
    private protected override string ManifestText => "version: 2\nrouting:\n  preset: strict\n";

    [Fact]
    public async Task AClassThatNeedsASpecWithoutOneIsRefusedEvenWhenTheWorkingTreeLowersIt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.HeadAsync(cancellationToken);
        repository.WriteCode(Buggy, RoundingTest).Write("lockflight.yaml", "version: 2\nrouting:\n  preset: relaxed\n");

        (ExitCode exitCode, _, string error) = await RedAsync();

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("needs a spec: use the feature flow", error, StringComparison.Ordinal);
        Assert.Equal(head, await repository.HeadAsync(cancellationToken));
    }
}
