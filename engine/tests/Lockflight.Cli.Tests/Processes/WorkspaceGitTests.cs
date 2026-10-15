using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Processes;

/// <summary>A workspace shell (a git repository ignoring <c>services/</c>) with one child repository beneath it.</summary>
public sealed class WorkspaceGitTests : IDisposable
{
    private readonly DirectoryInfo shell = Directory.CreateTempSubdirectory("lockflight-wsgit-");

    private string Child => Path.Combine(shell.FullName, "services", "orders");

    [Fact]
    public async Task ChangesInAChildRepositoryAreReportedUnderItsUnitPath()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateWorkspaceAsync(cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Child, "Order.cs"), "class Order { int total; }\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Child, "New.cs"), "class New {}\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, "notes.cs"), "class Notes {}\n", cancellationToken);

        IReadOnlyList<string>? changed = await WorkspaceGit.ChangedNamesAsync(shell.FullName, Git.WorkingTree, Timeout.InfiniteTimeSpan, cancellationToken);
        IReadOnlyList<string>? untracked = await WorkspaceGit.UntrackedFilesAsync(shell.FullName, [], Timeout.InfiniteTimeSpan, cancellationToken);
        IReadOnlyDictionary<string, IReadOnlySet<int>>? lines = await ChangedLines.FindAsync(shell.FullName, baseRef: null, cancellationToken);

        Assert.Equal(["services/orders/Order.cs"], changed);
        Assert.Equal(["notes.cs", "services/orders/New.cs"], untracked!.Order(StringComparer.Ordinal));
        Assert.Equal([1], lines!["services/orders/Order.cs"]);
        Assert.True(lines.ContainsKey("services/orders/New.cs"));
    }

    [Fact]
    public async Task AnEditInAChildRepositoryChangesTheStopCacheFingerprint()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateWorkspaceAsync(cancellationToken);

        string? clean = await InputsHash.ComputeAsync(shell.FullName, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Child, "Order.cs"), "class Order { int total; }\n", cancellationToken);
        string? edited = await InputsHash.ComputeAsync(shell.FullName, cancellationToken);

        Assert.NotNull(clean);
        Assert.NotEqual(clean, edited);
    }

    [Fact]
    public async Task AUnitWithoutItsOwnRepositoryIsPartOfTheShellsGit()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateWorkspaceAsync(cancellationToken);
        Directory.CreateDirectory(Path.Combine(shell.FullName, "libs", "shared"));

        IReadOnlyList<RepoRoot> repos = WorkspaceRepos.Of(shell.FullName);

        Assert.Equal(["", "services/orders/"], repos.Select(repo => repo.Prefix));
    }

    private async Task CreateWorkspaceAsync(CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, Manifest.FileName), "version: 1\nunits:\n  - path: services/orders\n  - path: libs/shared\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, ".gitignore"), "services/\n", cancellationToken);
        await GitAsync(shell.FullName, ["init", "-q"]);
        await GitAsync(shell.FullName, ["add", "."]);
        await GitAsync(shell.FullName, ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "shell"]);
        Directory.CreateDirectory(Child);
        await File.WriteAllTextAsync(Path.Combine(Child, "Order.cs"), "class Order {}\n", cancellationToken);
        await GitAsync(Child, ["init", "-q"]);
        await GitAsync(Child, ["add", "."]);
        await GitAsync(Child, ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "orders"]);
    }

    private static async Task GitAsync(string directory, string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, directory, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(shell);
}
