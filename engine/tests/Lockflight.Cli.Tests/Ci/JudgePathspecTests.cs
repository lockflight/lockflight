using Lockflight.Cli.Ci;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Ci;

public sealed class JudgePathspecTests : IAsyncLifetime
{
    private GitHistoryFixture repository = null!;

    public async ValueTask InitializeAsync() => repository = await GitHistoryFixture.CreateAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ALaidOverFileNamedLikeAPatternReplacesOnlyItself()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows file names cannot hold '*'.");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string forkPoint = await repository.Write("src/Foo.cs", "fork point\n").CommitAsync("fork point", cancellationToken);
        string head = await repository.Write("src/Foo.cs", "head\n").Write("*/Foo.cs", "test side\n").CommitAsync("head", cancellationToken);
        using GitHistoryFixture parent = await GitHistoryFixture.CreateAsync(cancellationToken);
        JudgeWorktree worktree = (await JudgeWorktree.OpenAsync(repository.Root, parent.Root, forkPoint, TimeSpan.FromSeconds(60), cancellationToken))!;
        try
        {
            Assert.Null(await worktree.MoveToAsync(forkPoint, forkPoint, clean: true, cancellationToken));

            Assert.Null(await worktree.OverlayAsync(head, ["*/Foo.cs"], [], forkPoint, cancellationToken));

            Assert.Equal("test side\n", File.ReadAllText(Path.Combine(worktree.Root, "*", "Foo.cs")));
            Assert.Equal("fork point\n", File.ReadAllText(Path.Combine(worktree.Root, "src", "Foo.cs")));
        }
        finally
        {
            await worktree.RemoveAsync();
        }
    }

    [Fact]
    public async Task ADiffOfAFileNamedLikePathspecMagicShowsOnlyThatFile()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows file names cannot hold ':' or '*'.");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string from = await repository.Write("src/a.txt", "a\n").Write("x/Directory.Packages.props", "<Project />\n").CommitAsync("base", cancellationToken);
        string to = await repository.Write("src/a.txt", "b\n").Write("x/Directory.Packages.props", "<Project>changed</Project>\n")
            .Write("*/a.txt", "star\n").Write(":(exclude)x/Directory.Packages.props", "magic\n").CommitAsync("head", cancellationToken);

        string? star = await JudgeGit.DiffHunksAsync(repository.Root, from, to, ["*/a.txt"], cancellationToken);
        string? magic = await JudgeGit.DiffHunksAsync(repository.Root, from, to, [":(exclude)x/Directory.Packages.props"], cancellationToken);

        Assert.Equal(["*/a.txt"], UnifiedDiff.FilesOf(star!).Select(file => file.File));
        Assert.Equal([":(exclude)x/Directory.Packages.props"], UnifiedDiff.FilesOf(magic!).Select(file => file.File));
    }
}
