using Lockflight.Cli.Gates;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates;

public sealed class InputsHashTests : IDisposable
{
    private readonly DirectoryInfo repository = Directory.CreateTempSubdirectory("lockflight-hash-");

    [Fact]
    public async Task TheHashIsStableUntilTrackedOrNewFilesChange()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await GitAsync(["init", "-q"]);
        await File.WriteAllTextAsync(Path.Combine(repository.FullName, "a.cs"), "class A {}", cancellationToken);
        await GitAsync(["add", "."]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "init"]);

        string? clean = await InputsHash.ComputeAsync(repository.FullName, cancellationToken);
        string? cleanAgain = await InputsHash.ComputeAsync(repository.FullName, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repository.FullName, "a.cs"), "class A { int x; }", cancellationToken);
        string? edited = await InputsHash.ComputeAsync(repository.FullName, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repository.FullName, "b.cs"), "class B {}", cancellationToken);
        string? withNewFile = await InputsHash.ComputeAsync(repository.FullName, cancellationToken);

        Assert.NotNull(clean);
        Assert.Equal(clean, cleanAgain);
        Assert.NotEqual(clean, edited);
        Assert.NotEqual(edited, withNewFile);
    }

    [Fact]
    public async Task OutsideAGitRepositoryThereIsNoHash() =>
        Assert.Null(await InputsHash.ComputeAsync(repository.FullName, TestContext.Current.CancellationToken));

    [Fact]
    public async Task OnlyTheRememberedPassHashCountsAsCached()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        StopGateCache cache = new(repository.FullName, unitPath: null);

        await cache.RememberPassAsync("abc", cancellationToken);

        Assert.True(await cache.PassedWithAsync("abc", cancellationToken));
        Assert.False(await cache.PassedWithAsync("def", cancellationToken));
    }

    [Fact]
    public async Task ChangedGateSettingsChangeTheHashEvenWhenCommitted()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await GitAsync(["init", "-q"]);
        await File.WriteAllTextAsync(Path.Combine(repository.FullName, ".gitignore"), "lockflight.yaml\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repository.FullName, "lockflight.yaml"), "version: 1\n", cancellationToken);
        await GitAsync(["add", "."]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "init"]);

        string? before = await InputsHash.ComputeAsync(repository.FullName, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repository.FullName, "lockflight.yaml"), "version: 1\ngates:\n  timeoutSeconds: 120\n", cancellationToken);
        string? after = await InputsHash.ComputeAsync(repository.FullName, cancellationToken);

        Assert.NotNull(before);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task AnUnreadableCacheIsAMissNotACrash()
    {
        Directory.CreateDirectory(Path.Combine(repository.FullName, StopGateCache.RelativePath));

        Assert.False(await new StopGateCache(repository.FullName, unitPath: null).PassedWithAsync("abc", TestContext.Current.CancellationToken));
    }

    private Task<ProcessResult> GitAsync(string[] arguments) =>
        ProcessRunner.RunAsync("git", arguments, repository.FullName, TestContext.Current.CancellationToken);

    public void Dispose() => TempDirectories.DeleteRecursively(repository);
}
