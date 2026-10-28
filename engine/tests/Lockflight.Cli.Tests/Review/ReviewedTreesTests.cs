using Lockflight.Cli.Processes;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReviewedTreesTests : IDisposable
{
    private const string SpecPath = "specs/001-orders/spec.md";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-reviewed-tree-");

    [Fact]
    public async Task ACommittedTreeIsClean()
    {
        await RepositoryAsync();

        ReviewedTree tree = await FindAsync();

        Assert.True(tree.IsClean);
    }

    [Fact]
    public async Task ChangedAndStagedFilesAreUncommitted()
    {
        await RepositoryAsync();
        Write("src/Orders.cs", "namespace Shop; // changed\n");
        Write("src/Prices.cs", "namespace Shop; // staged\n");
        await GitAsync("add", "src/Prices.cs");

        ReviewedTree tree = await FindAsync();

        Assert.False(tree.IsClean);
        Assert.Equal(["src/Orders.cs", "src/Prices.cs"], tree.Uncommitted);
        Assert.Empty(tree.Untracked);
    }

    [Fact]
    public async Task AnUntrackedFileCountsAndAnIgnoredOneDoesNot()
    {
        await RepositoryAsync();
        Write("src/New.cs", "namespace Shop;\n");
        Write("notes.local", "mine\n");

        ReviewedTree tree = await FindAsync();

        Assert.Equal(["src/New.cs"], tree.Untracked);
        Assert.Empty(tree.Uncommitted);
    }

    [Fact]
    public async Task TheEnginesOwnPathsAndBuildOutputDoNotCount()
    {
        await RepositoryAsync();
        Write(".lockflight/features/001-orders/reviews/run.json", "{}\n");
        Write(".lockflight/redgreen.json", "{}\n");
        Write(".claude/worktrees/agent-1/src/Orders.cs", "namespace Shop;\n");
        Write("src/Shop/bin/Debug/Shop.dll", "binary\n");
        Write("src/Shop/obj/project.assets.json", "{}\n");

        ReviewedTree tree = await FindAsync();

        Assert.True(tree.IsClean);
    }

    [Fact]
    public async Task TheFeaturesSpecCountsAndOtherSpecFilesDoNot()
    {
        await RepositoryAsync();
        Write(SpecPath, "- **FR-001**: The total applies the discount, changed.\n");
        Write("specs/002-other/spec.md", "- **FR-001**: Another feature.\n");
        Write("specs/001-orders/tasks.md", "- [x] T001 done\n");

        ReviewedTree tree = await FindAsync();

        Assert.Equal([SpecPath], tree.Uncommitted);
        Assert.Empty(tree.Untracked);
    }

    [Fact]
    public async Task OutsideARepositoryTheTreeIsUnchecked()
    {
        ReviewedTree tree = await FindAsync();

        Assert.False(tree.Checked);
        Assert.False(tree.IsClean);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private Task<ReviewedTree> FindAsync() =>
        ReviewedTrees.FindAsync(root.FullName, Path.Combine(root.FullName, SpecPath.Replace('/', Path.DirectorySeparatorChar)), TestContext.Current.CancellationToken);

    private async Task RepositoryAsync()
    {
        await GitAsync("init", "-q", "-b", "main");
        await GitAsync("config", "user.email", "dev@example.com");
        await GitAsync("config", "user.name", "Dev");
        await GitAsync("config", "commit.gpgsign", "false");
        Write(".gitignore", "*.local\n");
        Write(SpecPath, "- **FR-001**: The total applies the discount.\n");
        Write("specs/001-orders/tasks.md", "- [ ] T001 todo\n");
        Write("src/Orders.cs", "namespace Shop;\n");
        Write("src/Prices.cs", "namespace Shop;\n");
        await GitAsync("add", ".");
        await GitAsync("commit", "-q", "-m", "start");
    }

    private void Write(string path, string content)
    {
        string full = Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private async Task GitAsync(params string[] arguments) =>
        await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
}
