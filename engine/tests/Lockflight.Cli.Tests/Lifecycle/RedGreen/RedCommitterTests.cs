using Lockflight.Cli.Lifecycle.RedGreen.Snapshots;
using Lockflight.Cli.Tests.Ci;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class RedCommitterTests : IAsyncLifetime
{
    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(60);

    private GitHistoryFixture repository = null!;

    public async ValueTask InitializeAsync()
    {
        repository = await GitHistoryFixture.CreateAsync(TestContext.Current.CancellationToken);
        await repository.Write("src/Shop/Shop.csproj", "<Project />\n").Write("tests/Shop.Tests/Shop.Tests.csproj", "<Project />\n").Write("README.md", "# shop\n")
            .CommitAsync("init", TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task TheRedIsCommittedWithItsTrailerAndWhatThePersonStagedStaysStaged()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository.Write("README.md", "# shop, staged\n");
        await repository.GitAsync(["add", "README.md"], cancellationToken);
        repository.Write("README.md", "# shop, staged, then edited\n").Write("tests/Shop.Tests/PriceTests.cs", "class PriceTests {}\n").Write("src/Shop/Price.cs", "class Price {}\n");
        RedSnapshot snapshot = await RedSnapshots.TakeAsync(repository.Root, [repository.Root], TimeLimit, cancellationToken);

        RedCommitOutcome outcome = await RedCommitter.CommitAsync(repository.Root, snapshot.Commit!, "001-shipping", 2, TimeLimit, cancellationToken);

        Assert.NotNull(outcome.Commit);
        Assert.Contains("the tree this red judged", outcome.Message, StringComparison.Ordinal);
        string message = await repository.GitAsync(["log", "-1", "--format=%s%n%(trailers:key=Lockflight-Red,valueonly)"], cancellationToken);
        Assert.Equal("test(001-shipping): red for cycle 2\n2\n", message.ReplaceLineEndings("\n").TrimEnd('\n') + "\n");
        Assert.Contains("tests/Shop.Tests/PriceTests.cs", await repository.GitAsync(["show", "--name-only", "--format=", "HEAD"], cancellationToken), StringComparison.Ordinal);
        Assert.Equal(string.Empty, (await repository.GitAsync(["diff", "--cached", "--name-only"], cancellationToken)).Trim());
    }

    [Fact]
    public async Task UntrackedFilesOutsideTheProjectsAreRefusedBeforeTheBuild()
    {
        repository.Write("tests/Shop.Tests/PriceTests.cs", "class PriceTests {}\n").Write("notes/scratch.txt", "secret\n");

        string? refusal = await RedCommitter.RefusalAsync(repository.Root, TimeLimit, TestContext.Current.CancellationToken);

        Assert.NotNull(refusal);
        Assert.Contains("notes/scratch.txt", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("PriceTests.cs", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADetachedHeadIsRefused()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.GitAsync(["checkout", "-q", "--detach"], cancellationToken);

        Assert.Contains("HEAD is detached", await RedCommitter.RefusalAsync(repository.Root, TimeLimit, cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectFilesSpecsAndRecordsAreNoReasonToRefuse()
    {
        repository.Write("tests/Shop.Tests/PriceTests.cs", "x").Write("specs/001-shipping/spec.md", "x").Write(".lockflight/features/001-shipping/redgreen.json", "{}");

        Assert.Null(await RedCommitter.RefusalAsync(repository.Root, TimeLimit, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARedIsNotCommittedOntoACommitMadeWhileItRan()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository.Write("tests/Shop.Tests/PriceTests.cs", "class PriceTests {}\n");
        RedSnapshot snapshot = await RedSnapshots.TakeAsync(repository.Root, [repository.Root], TimeLimit, cancellationToken);
        string moved = await repository.Write("README.md", "# shop, moved on\n").CommitAsync("other work", cancellationToken);

        RedCommitOutcome outcome = await RedCommitter.CommitAsync(repository.Root, snapshot.Commit!, "001-shipping", 1, TimeLimit, cancellationToken);

        Assert.Null(outcome.Commit);
        Assert.Contains("HEAD moved", outcome.Message, StringComparison.Ordinal);
        Assert.Equal(moved, await repository.HeadAsync(cancellationToken));
    }

    [Fact]
    public async Task UntrackedFilesAboveAProjectRootBelowTheTopLevelAreRefused()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.Write("app/src/Shop/Shop.csproj", "<Project />\n").CommitAsync("app", cancellationToken);
        repository.Write("app/src/Shop/Price.cs", "class Price {}\n").Write("app/specs/001-shipping/spec.md", "x").Write("scratch.txt", "secret\n");

        string? refusal = await RedCommitter.RefusalAsync(Path.Combine(repository.Root, "app"), TimeLimit, cancellationToken);

        Assert.NotNull(refusal);
        Assert.Contains("scratch.txt", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("Price.cs", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("spec.md", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLineGivenWhenAHookRefusesCommitsTheJudgedTreeNotTheCurrentOne()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository.Write(".git/hooks/pre-commit", "#!/bin/sh\nexit 1\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path.Combine(repository.Root, ".git", "hooks", "pre-commit"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        repository.Write("tests/Shop.Tests/PriceTests.cs", "class PriceTests {}\n");
        RedSnapshot snapshot = await RedSnapshots.TakeAsync(repository.Root, [repository.Root], TimeLimit, cancellationToken);
        repository.Write("src/Shop/Price.cs", "class Price { /* written after the red */ }\n");

        RedCommitOutcome outcome = await RedCommitter.CommitAsync(repository.Root, snapshot.Commit!, "001-shipping", 1, TimeLimit, cancellationToken);

        Assert.Null(outcome.Commit);
        Assert.Contains($"git read-tree {snapshot.Commit} && git commit --no-verify", outcome.Message, StringComparison.Ordinal);
        await repository.GitAsync(["read-tree", snapshot.Commit!], cancellationToken);
        await repository.GitAsync(["commit", "-q", "--no-verify", "-m", "red"], cancellationToken);
        Assert.Equal(await repository.CommitOfAsync($"{snapshot.Commit}^{{tree}}", cancellationToken), await repository.CommitOfAsync("HEAD^{tree}", cancellationToken));
    }

    [Fact]
    public async Task AFirstCommitKeepsTheIndexInStep()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using GitHistoryFixture empty = await GitHistoryFixture.CreateAsync(cancellationToken);
        empty.Write("src/Shop/Shop.csproj", "<Project />\n").Write("tests/Shop.Tests/PriceTests.cs", "class PriceTests {}\n");
        RedSnapshot snapshot = await RedSnapshots.TakeAsync(empty.Root, [empty.Root], TimeLimit, cancellationToken);

        RedCommitOutcome outcome = await RedCommitter.CommitAsync(empty.Root, snapshot.Commit!, "001-shipping", 1, TimeLimit, cancellationToken);

        Assert.NotNull(outcome.Commit);
        Assert.Equal(string.Empty, (await empty.GitAsync(["diff", "--cached", "--name-only"], cancellationToken)).Trim());
    }
}
