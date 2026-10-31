using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Routing;

public sealed class ChangeSetReaderTests
{
    private const string Code = "class Order\n{\n    int Total;\n    int Tax;\n    int Count;\n}\n";

    private static readonly Dictionary<string, string> BaseFiles = new(StringComparer.Ordinal)
    {
        ["src/Shop/Shop.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
        ["src/Shop/Order.cs"] = Code,
    };

    [Fact]
    public async Task ADeletedFileKeepsItsRemovedLinesWithTheirBaseNumbers()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        repository.Delete("src/Shop/Order.cs");
        await repository.CommitAsync("delete", cancellationToken);

        ChangeSet changeSet = await ReadAsync(repository, "HEAD", cancellationToken);

        ChangedFile file = Assert.Single(changeSet.Files);
        Assert.Equal(ChangeKind.Deleted, file.Kind);
        Assert.Null(file.NewBlob);
        Assert.Equal([1, 2, 3, 4, 5, 6], file.Removed.Select(line => line.Line));
        Assert.Equal("    int Total;", file.Removed[2].Text);
        Assert.Equal(Code, changeSet.Base.TextOf("src/Shop/Order.cs"));
        Assert.False(changeSet.Head.Contains("src/Shop/Order.cs"));
    }

    [Fact]
    public async Task TheUsersDiffAlgorithmDoesNotChangeTheLines()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string before = "a\nb\nc\nd\na\nb\nc\nd\n{\n}\nx\n{\n}\n";
        const string after = "a\nc\nd\nb\na\nb\nd\nc\n{\n}\n{\n}\ny\nx\n";
        using RoutingRepository repository = await RoutingRepository.CreateAsync(new Dictionary<string, string> { ["notes.txt"] = before }, cancellationToken);
        repository.Write("notes.txt", after);
        await repository.CommitAsync("reorder", cancellationToken);

        ChangedFile myers = Assert.Single((await ReadAsync(repository, "HEAD", cancellationToken)).Files);
        foreach (string algorithm in new[] { "histogram", "patience", "minimal" })
        {
            await repository.GitAsync(["config", "diff.algorithm", algorithm], cancellationToken);
            ChangedFile configured = Assert.Single((await ReadAsync(repository, "HEAD", cancellationToken)).Files);

            Assert.Equal(myers.Added, configured.Added);
            Assert.Equal(myers.Removed, configured.Removed);
        }
    }

    [Fact]
    public async Task ExternalDiffDriversAndTextConversionAreNotUsed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        repository.Write(".gitattributes", "*.cs diff=hide\n");
        await repository.GitAsync(["config", "diff.hide.textconv", "false"], cancellationToken);
        await repository.GitAsync(["config", "diff.external", "false"], cancellationToken);
        repository.Write("src/Shop/Order.cs", Code.Replace("int Tax;", "int Fee;", StringComparison.Ordinal));
        await repository.CommitAsync("edit", cancellationToken);

        ChangeSet changeSet = await ReadAsync(repository, "HEAD", cancellationToken);

        ChangedFile order = changeSet.Files.Single(file => file.Path == "src/Shop/Order.cs");
        Assert.Equal(["    int Fee;"], order.Added.Select(line => line.Text));
        Assert.Equal(["    int Tax;"], order.Removed.Select(line => line.Text));
    }

    [Fact]
    public async Task ABinaryFileIsMarkedAndHasNoLinesOrText()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        repository.WriteBytes("src/Shop/icon.png", [137, 80, 78, 71, 0, 0, 1]);
        await repository.CommitAsync("icon", cancellationToken);

        ChangedFile icon = Assert.Single((await ReadAsync(repository, "HEAD", cancellationToken)).Files);

        Assert.True(icon.Binary);
        Assert.Empty(icon.Added);
        Assert.NotNull(icon.NewBlob);
    }

    [Theory]
    [InlineData("*.cs -diff\n")]
    [InlineData("*.cs binary\n")]
    public async Task AChangesOwnGitAttributesDoNotTurnItsSourceIntoABinaryFile(string attributes)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        repository.Write(".gitattributes", attributes);
        repository.Write("src/Shop/Order.cs", Code.Replace("int Tax;", "int Fee;", StringComparison.Ordinal));
        await repository.CommitAsync("hide", cancellationToken);

        ChangeSet changeSet = await ReadAsync(repository, "HEAD", cancellationToken);

        ChangedFile order = changeSet.Files.Single(file => file.Path == "src/Shop/Order.cs");
        Assert.False(order.Binary);
        Assert.Equal(["    int Fee;"], order.Added.Select(line => line.Text));
        Assert.Equal(["    int Tax;"], order.Removed.Select(line => line.Text));
    }

    [Fact]
    public async Task ABinaryFileMarkedByAttributesStaysBinary()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        repository.Write(".gitattributes", "*.png -diff\n");
        repository.WriteBytes("src/Shop/icon.png", [137, 80, 78, 71, 0, 0, 1]);
        await repository.CommitAsync("icon", cancellationToken);

        ChangedFile icon = (await ReadAsync(repository, "HEAD", cancellationToken)).Files.Single(file => file.Path == "src/Shop/icon.png");

        Assert.True(icon.Binary);
        Assert.Empty(icon.Added);
    }

    [Fact]
    public async Task UntrackedFilesCountLocallyWithEveryLineAndTheBlobGitWouldStore()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        repository.Write("src/Shop/Cart.cs", "class Cart\n{\n}\n");
        string stored = (await repository.GitAsync(["hash-object", "src/Shop/Cart.cs"], cancellationToken)).Trim();

        ChangeSet changeSet = await ReadAsync(repository, headRef: null, cancellationToken);

        ChangedFile cart = Assert.Single(changeSet.Files);
        Assert.Equal(ChangeKind.Added, cart.Kind);
        Assert.Equal([1, 2, 3], cart.Added.Select(line => line.Line));
        Assert.Equal(stored, cart.NewBlob);
        Assert.Null(changeSet.HeadCommit);
        Assert.True(changeSet.Head.Contains("src/Shop/Cart.cs"));
    }

    [Fact]
    public async Task ALocalRunSeesCommittedAndUncommittedChangesOnce()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        repository.Write("src/Shop/Order.cs", Code.Replace("int Tax;", "int Fee;", StringComparison.Ordinal));
        await repository.CommitAsync("first", cancellationToken);
        repository.Write("src/Shop/Order.cs", Code.Replace("int Tax;", "int Levy;", StringComparison.Ordinal));

        ChangedFile order = Assert.Single((await ReadAsync(repository, headRef: null, cancellationToken)).Files);

        Assert.Equal(["    int Levy;"], order.Added.Select(line => line.Text));
        Assert.Equal(["    int Tax;"], order.Removed.Select(line => line.Text));
    }

    [Fact]
    public async Task TheDiffStartsAtTheMergeBaseNotAtTheBaseTip()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        repository.Write("src/Shop/Cart.cs", "class Cart { }\n");
        string head = await repository.CommitAsync("cart", cancellationToken);
        await repository.GitAsync(["checkout", "-q", RoutingRepository.BaseBranch], cancellationToken);
        repository.Write("src/Shop/Other.cs", "class Other { }\n");
        string baseTip = await repository.CommitAsync("moved on", cancellationToken);

        ChangeSet changeSet = await ReadAsync(repository, head, cancellationToken);

        Assert.Equal(["src/Shop/Cart.cs"], changeSet.Files.Select(file => file.Path));
        Assert.Equal(baseTip, changeSet.BaseCommit);
        Assert.NotEqual(baseTip, changeSet.MergeBase);
        Assert.Equal(head, changeSet.HeadCommit);
    }

    [Fact]
    public async Task ProjectFilesAndPropsAreReadOnBothSides()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(
            new Dictionary<string, string>(BaseFiles) { ["Directory.Build.props"] = "<Project />\n", ["src/Shop/Order.Designer.cs"] = "class D { }\n" }, cancellationToken);
        repository.Write("src/Shop/Order.cs", Code + "\n");
        await repository.CommitAsync("edit", cancellationToken);

        ChangeSet changeSet = await ReadAsync(repository, "HEAD", cancellationToken);

        foreach (RevisionSnapshot side in new[] { changeSet.Base, changeSet.Head })
        {
            Assert.Equal("<Project Sdk=\"Microsoft.NET.Sdk\" />\n", side.TextOf("src/Shop/Shop.csproj"));
            Assert.Equal("<Project />\n", side.TextOf("Directory.Build.props"));
            Assert.Null(side.TextOf("src/Shop/Order.Designer.cs"));
            Assert.True(side.Contains("src/Shop/Order.Designer.cs"));
        }
    }

    [Fact]
    public async Task AWorkspaceChildRepositoryIsReadUnderItsUnitPath()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository shell = await RoutingRepository.CreateAsync(
            new Dictionary<string, string> { [Manifest.FileName] = "version: 1\nunits:\n  - path: services/orders\n", [".gitignore"] = "services/\n" }, cancellationToken);
        string child = Path.Combine(shell.Root, "services", "orders");
        Directory.CreateDirectory(child);
        await File.WriteAllTextAsync(Path.Combine(child, "Order.cs"), "class Order { }\n", cancellationToken);
        await ChildGitAsync(child, ["init", "-q", "-b", RoutingRepository.BaseBranch], cancellationToken);
        await ChildGitAsync(child, ["add", "."], cancellationToken);
        await ChildGitAsync(child, ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-qm", "orders"], cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(child, "Order.cs"), "class Order { int Total; }\n", cancellationToken);

        ChangeSet changeSet = await ReadAsync(shell, headRef: null, cancellationToken);

        ChangedFile order = Assert.Single(changeSet.Files);
        Assert.Equal("services/orders/Order.cs", order.Path);
        Assert.Equal("class Order { }\n", changeSet.Base.TextOf("services/orders/Order.cs"));
        Assert.True(changeSet.Head.Contains("services/orders/Order.cs"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0000000000000000000000000000000000000000")]
    [InlineData("no-such-branch")]
    public async Task AMissingBaseIsNoBaseNeverAnEmptyChange(string baseRef)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);

        ChangeSetReading reading = await ChangeSetReader.ReadAsync(repository.Root, baseRef, "HEAD", cancellationToken);

        Assert.Equal(ChangeSetReadStatus.NoBase, reading.Status);
        Assert.Null(reading.ChangeSet);
        Assert.NotEmpty(reading.Message);
    }

    [Fact]
    public async Task AnUnknownHeadIsAGitFailure()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);

        ChangeSetReading reading = await ChangeSetReader.ReadAsync(repository.Root, RoutingRepository.BaseBranch, "deadbeef", cancellationToken);

        Assert.Equal(ChangeSetReadStatus.GitFailed, reading.Status);
        Assert.Contains("deadbeef", reading.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnrelatedHistoriesAreAGitFailure()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        await repository.GitAsync(["checkout", "-q", "--orphan", "lonely"], cancellationToken);
        repository.Write("other.txt", "x\n");
        await repository.CommitAsync("lonely", cancellationToken);

        ChangeSetReading reading = await ChangeSetReader.ReadAsync(repository.Root, RoutingRepository.BaseBranch, "HEAD", cancellationToken);

        Assert.Equal(ChangeSetReadStatus.GitFailed, reading.Status);
        Assert.Contains("no common commit", reading.Message, StringComparison.Ordinal);
    }

    private static async Task<ChangeSet> ReadAsync(RoutingRepository repository, string? headRef, CancellationToken cancellationToken)
    {
        ChangeSetReading reading = await ChangeSetReader.ReadAsync(repository.Root, RoutingRepository.BaseBranch, headRef, cancellationToken);
        Assert.True(reading.Status == ChangeSetReadStatus.Read, reading.Message);
        return reading.ChangeSet!;
    }

    private static async Task ChildGitAsync(string directory, string[] arguments, CancellationToken cancellationToken)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, directory, cancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }
}
