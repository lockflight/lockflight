using Lockflight.Cli.Lifecycle.RedGreen.Snapshots;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class RedSnapshotsTests : IDisposable
{
    private const string Feature = "20260101-000000-archive";

    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(60);

    private readonly DirectoryInfo repository = Directory.CreateTempSubdirectory("lockflight-snapshot-");

    [Fact]
    public async Task ASnapshotHoldsTheWorkingTreeAndLeavesTheIndexBranchAndStashAlone()
    {
        await InitAsync();
        Write("src/Order.cs", "class Order {}\n");
        Write("tests/OrderTests.cs", "class OrderTests {}\n");
        await CommitAllAsync();
        Write("src/Order.cs", "class Order { void Archive() {} }\n");
        Write("src/Staged.cs", "class Staged {}\n");
        await GitAsync(["add", "src/Staged.cs"]);
        Write("tests/ArchiveTests.cs", "class ArchiveTests {}\n");
        string statusBefore = await GitOutputAsync(["status", "--porcelain=v1", "--branch"]);
        string headBefore = await GitOutputAsync(["rev-parse", "HEAD"]);

        RedSnapshot snapshot = await TakeAsync();
        string? reference = await RedSnapshots.KeepAsync(repository.FullName, Feature, 1, snapshot.Commit!, TimeLimit, TestContext.Current.CancellationToken);

        Assert.Null(snapshot.NotTaken);
        Assert.Equal($"refs/lockflight/red/{Feature}/1/{snapshot.Commit![..12]}", reference);
        Assert.Equal(snapshot.Commit, await GitOutputAsync(["rev-parse", reference!]));
        Assert.Equal("class Order { void Archive() {} }\n", await GitOutputAsync(["show", $"{snapshot.Commit}:src/Order.cs"], trim: false));
        Assert.Equal("class ArchiveTests {}\n", await GitOutputAsync(["show", $"{snapshot.Commit}:tests/ArchiveTests.cs"], trim: false));
        Assert.Equal("class Staged {}\n", await GitOutputAsync(["show", $"{snapshot.Commit}:src/Staged.cs"], trim: false));
        Assert.Equal(statusBefore, await GitOutputAsync(["status", "--porcelain=v1", "--branch"]));
        Assert.Equal(headBefore, await GitOutputAsync(["rev-parse", "HEAD"]));
        Assert.Equal(string.Empty, await GitOutputAsync(["stash", "list"]));
        Assert.Equal(headBefore, await GitOutputAsync(["rev-parse", $"{snapshot.Commit}^"]));
    }

    [Fact]
    public async Task ASnapshotLeavesBuildOutputAndTheLinkedEngineOut()
    {
        await InitAsync();
        Write("src/Order.cs", "class Order {}\n");
        await CommitAllAsync();
        Write("src/bin/Debug/Order.dll", "binary");
        Write("src/obj/project.assets.json", "{}");
        Write(".lockflight/bin/lockflight", "engine");
        Write(".claude/worktrees/agent/src/Order.cs", "class Order {}\n");

        RedSnapshot snapshot = await TakeAsync();

        Assert.Equal("src/Order.cs", await GitOutputAsync(["ls-tree", "-r", "--name-only", snapshot.Commit!]));
    }

    [Fact]
    public async Task ASnapshotWorksBeforeTheFirstCommit()
    {
        await InitAsync();
        Write("src/Order.cs", "class Order {}\n");

        RedSnapshot snapshot = await TakeAsync();

        Assert.Null(snapshot.NotTaken);
        Assert.Equal("src/Order.cs", await GitOutputAsync(["ls-tree", "-r", "--name-only", snapshot.Commit!]));
    }

    [Fact]
    public async Task UntrackedFilesThatAreNotSourceAreNamed()
    {
        await InitAsync();
        Write("src/Order.cs", "class Order {}\n");
        await CommitAllAsync();
        Write("tests/ArchiveTests.cs", "class ArchiveTests {}\n");
        Write("notes/token.txt", "secret\n");

        RedSnapshot snapshot = await TakeAsync();

        Assert.Equal(["notes/token.txt"], snapshot.UntrackedNonSource);
    }

    [Fact]
    public async Task NoSnapshotIsTakenOutsideAGitRepository()
    {
        Write("src/Order.cs", "class Order {}\n");

        RedSnapshot snapshot = await TakeAsync();

        Assert.Null(snapshot.Commit);
        Assert.Contains("not a git repository", snapshot.NotTaken, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoSnapshotIsTakenWhenABuildDirectoryBelongsToAnotherRepository()
    {
        await InitAsync();
        Write("README.md", "workspace\n");
        await CommitAllAsync();
        string unit = Path.Combine(repository.FullName, "services", "orders");
        Directory.CreateDirectory(unit);
        await ProcessRunner.RunAsync("git", ["init", "-q"], unit, TestContext.Current.CancellationToken);

        RedSnapshot snapshot = await RedSnapshots.TakeAsync(repository.FullName, [unit], TimeLimit, TestContext.Current.CancellationToken);

        Assert.Null(snapshot.Commit);
        Assert.Contains("services/orders", snapshot.NotTaken, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWorktreeOfTheSnapshotHoldsItsFilesUnderTheRootAndIsRemovedAfterwards()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await InitAsync();
        Write("app/src/Order.cs", "class Order {}\n");
        await CommitAllAsync();
        Write("app/src/Order.cs", "class Order { int Stub() => throw null; }\n");
        string root = Path.Combine(repository.FullName, "app");
        RedSnapshot snapshot = await RedSnapshots.TakeAsync(root, [root], TimeLimit, cancellationToken);
        Write("app/src/Order.cs", "class Order { int Stub() => 42; }\n");

        SnapshotWorktreeOpening opening = await SnapshotWorktree.OpenAsync(root, snapshot.Commit!, TimeLimit, cancellationToken);
        SnapshotWorktree worktree = Assert.IsType<SnapshotWorktree>(opening.Worktree);
        // A checkout may convert line endings (core.autocrlf on Windows); the content is what counts.
        string checkedOut = (await File.ReadAllTextAsync(Path.Combine(worktree.Root, "src", "Order.cs"), cancellationToken)).ReplaceLineEndings("\n");
        bool insideGitDirectory = worktree.Root.Replace('\\', '/').Contains("/.git/lockflight/", StringComparison.Ordinal);
        string? cleanup = await worktree.RemoveAsync();

        Assert.Equal("class Order { int Stub() => throw null; }\n", checkedOut);
        Assert.True(insideGitDirectory, worktree.Root);
        Assert.Null(cleanup);
        Assert.False(Directory.Exists(worktree.Root));
        Assert.Single((await GitOutputAsync(["worktree", "list", "--porcelain"])).Split('\n'), line => line.StartsWith("worktree ", StringComparison.Ordinal));
        Assert.Equal("class Order { int Stub() => 42; }\n", await File.ReadAllTextAsync(Path.Combine(root, "src", "Order.cs"), cancellationToken));
    }

    [Fact]
    public async Task AWorktreeOfAMissingSnapshotIsRefused()
    {
        await InitAsync();
        Write("src/Order.cs", "class Order {}\n");
        await CommitAllAsync();

        SnapshotWorktreeOpening opening = await SnapshotWorktree.OpenAsync(
            repository.FullName, "0123456789abcdef0123456789abcdef01234567", TimeLimit, TestContext.Current.CancellationToken);

        Assert.Null(opening.Worktree);
        Assert.Contains("0123456", opening.Problem, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(repository);

    private Task<RedSnapshot> TakeAsync() =>
        RedSnapshots.TakeAsync(repository.FullName, [repository.FullName], TimeLimit, TestContext.Current.CancellationToken);

    private async Task InitAsync() => await GitAsync(["init", "-q", "-b", "main"]);

    private async Task CommitAllAsync()
    {
        await GitAsync(["add", "-A"]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-qm", "change"]);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(repository.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<string> GitOutputAsync(string[] arguments, bool trim = true)
    {
        ProcessResult result = await GitAsync(arguments);
        return trim ? result.Output.Trim() : result.Output;
    }

    private Task<ProcessResult> GitAsync(string[] arguments) =>
        ProcessRunner.RunAsync("git", arguments, repository.FullName, TestContext.Current.CancellationToken);
}
