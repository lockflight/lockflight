using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Processes;

public sealed class GitTests : IDisposable
{
    private readonly DirectoryInfo repository = Directory.CreateTempSubdirectory("lockflight-git-");

    [Fact]
    public async Task UntrackedFilesExcludeIgnoredAndTrackedFiles()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await InitAsync();
        Write(".gitignore", "local/\n");
        await CommitAllAsync();
        Write("src/New Thing.cs", "class NewThing {}");
        Write("local/ignored.txt", "x");

        IReadOnlyList<string>? untracked = await Git.UntrackedFilesAsync(repository.FullName, cancellationToken);

        Assert.Equal(["src/New Thing.cs"], untracked);
    }

    [Fact]
    public async Task OutsideARepositoryTheUntrackedFilesAreUnknown() =>
        Assert.Null(await Git.UntrackedFilesAsync(repository.FullName, TestContext.Current.CancellationToken));

    [Fact]
    public async Task ChangedNamesAreUnquotedRelativeToTheDirectoryAndListBothSidesOfARename()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await InitAsync();
        Write("app/specs/001-café/spec.md", "# Spec\n");
        Write("app/Old.cs", "class Old {}\n");
        Write("outside.txt", "x\n");
        await CommitAllAsync();
        await GitAsync(["checkout", "-qb", "feature"]);
        Write("app/specs/001-café/spec.md", "# Spec, changed\n");
        await GitAsync(["mv", "app/Old.cs", "app/New.cs"]);
        Write("outside.txt", "y\n");
        await CommitAllAsync();

        IReadOnlyList<string>? changed = await Git.ChangedNamesAsync(
            Path.Combine(repository.FullName, "app"), Git.SinceMergeBase("main"), Timeout.InfiniteTimeSpan, cancellationToken);

        Assert.Equal(["New.cs", "Old.cs", "specs/001-café/spec.md"], changed?.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnUnknownBaseLeavesTheChangedNamesUnknown()
    {
        await InitAsync();
        Write("a.txt", "a\n");
        await CommitAllAsync();

        Assert.Null(await Git.ChangedNamesAsync(repository.FullName, Git.SinceMergeBase("no-such-ref"), Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DiffHunksNameNonAsciiFilesPlainlyRelativeToTheDirectory()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await InitAsync();
        Write("app/src/Ünïcode Name.cs", "class A {}\n");
        await CommitAllAsync();
        Write("app/src/Ünïcode Name.cs", "class A {}\nclass B {}\n");

        string? diff = await Git.DiffHunksAsync(Path.Combine(repository.FullName, "app"), Git.WorkingTree, ["*.cs"], Timeout.InfiniteTimeSpan, cancellationToken);

        FileDiff file = Assert.Single(UnifiedDiff.FilesOf(diff!));
        Assert.Equal("src/Ünïcode Name.cs", file.File);
        Assert.Equal([2], file.Added.Select(line => line.Line));
    }

    [Fact]
    public async Task BeforeTheFirstCommitUncommittedChangesAreComparedWithTheEmptyTree()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await InitAsync();
        Write("a.txt", "added\n");
        await GitAsync(["add", "."]);

        string? uncommittedBase = await Git.UncommittedBaseAsync(repository.FullName, cancellationToken);
        string? diff = await Git.DiffHunksAsync(repository.FullName, uncommittedBase!, [], Timeout.InfiniteTimeSpan, cancellationToken);

        Assert.NotEqual(Git.WorkingTree, uncommittedBase);
        FileDiff file = Assert.Single(UnifiedDiff.FilesOf(diff!));
        Assert.Equal(["added"], file.Added.Select(line => line.Text));
    }

    [Fact]
    public async Task OutsideARepositoryThereIsNoUncommittedBase() =>
        Assert.Null(await Git.UncommittedBaseAsync(repository.FullName, TestContext.Current.CancellationToken));

    public void Dispose() => TempDirectories.DeleteRecursively(repository);

    private async Task InitAsync()
    {
        await GitAsync(["init", "-q", "-b", "main"]);
        await GitAsync(["config", "core.quotePath", "true"]);
    }

    private async Task CommitAllAsync()
    {
        await GitAsync(["add", "-A"]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "change"]);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(repository.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private Task<ProcessResult> GitAsync(string[] arguments) =>
        ProcessRunner.RunAsync("git", arguments, repository.FullName, TestContext.Current.CancellationToken);
}
