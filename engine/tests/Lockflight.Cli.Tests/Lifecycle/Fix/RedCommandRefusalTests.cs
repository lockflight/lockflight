using Lockflight.Cli.Hosting;
using Lockflight.Cli.Tests.Ci;

namespace Lockflight.Cli.Tests.Lifecycle.Fix;

/// <summary>The refusals that need no build: each exits with its code before anything is built, and HEAD does not move.</summary>
public sealed class RedCommandRefusalTests : RedCommandScenario
{
    private protected override string ManifestText => "version: 2\nci:\n  maxRedCommits: 1\n";

    [Fact]
    public async Task ARedOverTheBaseBranchsCapIsRefused()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.WriteCode(Buggy, RoundingTest).CommitAsync("test: red", cancellationToken, null, "Lockflight-Red: 1");
        // The working tree's manifest raises the cap: the base branch's value counts.
        repository.Write("lockflight.yaml", "version: 2\nci:\n  maxRedCommits: 5\n");

        (ExitCode exitCode, _, string error) = await RedAsync();

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("ci.maxRedCommits (1)", error, StringComparison.Ordinal);
        Assert.Equal(head, await repository.HeadAsync(cancellationToken));
    }

    [Fact]
    public async Task ARedoOfALabelTheBranchDoesNotHaveIsAConfigurationError()
    {
        (ExitCode exitCode, _, string error) = await RedAsync("--redo", "7");

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--redo 7", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABaseThatNamesNoCommitIsAConfigurationError()
    {
        (ExitCode exitCode, _, string error) = await RedAsync("--base", "nope");

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--base 'nope'", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADetachedHeadIsRefused()
    {
        await repository.GitAsync(["checkout", "-q", "--detach"], TestContext.Current.CancellationToken);

        (ExitCode exitCode, _, string error) = await RedAsync();

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("HEAD is detached", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RedIsOpenToTheAgent()
    {
        Assert.Equal(CommandAudience.Anyone, CommandRegistry.Match(["red"])!.Audience);
        Assert.False(HumanOnlyCommands.IsReserved(["red"]));
        Assert.False(HumanOnlyCommands.IsReserved(["red", "--redo", "1"]));
    }
}
