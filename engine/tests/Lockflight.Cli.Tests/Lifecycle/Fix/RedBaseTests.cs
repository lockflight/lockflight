using Lockflight.Cli.Config;
using Lockflight.Cli.Lifecycle.Fix;
using Lockflight.Cli.Tests.Ci;

namespace Lockflight.Cli.Tests.Lifecycle.Fix;

public sealed class RedBaseTests : IAsyncLifetime
{
    private GitHistoryFixture repository = null!;
    private string first = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        first = await repository.Write("lockflight.yaml", "version: 2\n").CommitAsync("init", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task TheDefaultTargetIsTheRemoteTrackingBranchWhenTheLocalOneIsBehindIt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string merged = await repository.Write("README.md", "merged\n").CommitAsync("merged on the remote", cancellationToken);
        await repository.GitAsync(["update-ref", "refs/remotes/origin/main", merged], cancellationToken);
        await repository.GitAsync(["reset", "-q", "--hard", first], cancellationToken);

        RedBaseResolution target = await RedBase.ResolveAsync(repository.Root, Manifest(), given: null, cancellationToken);

        Assert.Equal("origin/main", target.Reference);
        Assert.Equal(merged, target.Commit);
        Assert.Contains("not your local main", target.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutARemoteTheLocalBranchIsTheTarget()
    {
        RedBaseResolution target = await RedBase.ResolveAsync(repository.Root, Manifest(), given: null, TestContext.Current.CancellationToken);

        Assert.Equal("main", target.Reference);
        Assert.Equal(first, target.Commit);
        Assert.Null(target.Note);
    }

    [Fact]
    public async Task ABaseThatNamesNoCommitIsRefusedNamingTheOption()
    {
        RedBaseResolution target = await RedBase.ResolveAsync(repository.Root, Manifest(), "nope", TestContext.Current.CancellationToken);

        Assert.Null(target.Commit);
        Assert.Contains("--base 'nope'", target.Error, StringComparison.Ordinal);
    }

    private Manifest Manifest() => ManifestLocator.Discover(repository.Root).Manifest!;
}
