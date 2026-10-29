using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates.Baselines;

/// <summary><c>lockflight baseline suppressions</c> in a scratch repository whose default branch is <c>main</c>.</summary>
public sealed class SuppressionsBaselineTests : IDisposable
{
    private readonly DirectoryInfo repository = Directory.CreateTempSubdirectory("lockflight-suppressions-baseline-");

    [Fact]
    public async Task TheBranchsSuppressionsAreAcceptedByFileAndRuleAndEarlierOnesAreKept()
    {
        await InitRepositoryAsync();
        await GitAsync(["checkout", "-q", "-b", "feature"]);
        Write("src/Order.cs", "#pragma warning disable CA1822, CA2000\nclass Order { }\n");
        await CommitAsync("suppress");
        Write("src/Line.cs", "[ExcludeFromCodeCoverage]\nclass Line { }\n");
        new AcceptedSuppressions(repository.FullName).Save(["src/Other.cs|IDE0005"]);

        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(["baseline", "suppressions"], string.Empty, repository.FullName);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Equal("baseline: 4 suppressions (file and rule) accepted -> .lockflight/baselines/suppressions.json", output);
        Assert.Equal(
            ["src/Line.cs|ExcludeFromCodeCoverage", "src/Order.cs|CA1822", "src/Order.cs|CA2000", "src/Other.cs|IDE0005"],
            new AcceptedSuppressions(repository.FullName).Load().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task WithoutTheDefaultBranchNothingIsAcceptedAndThePersonIsTold()
    {
        await InitRepositoryAsync();
        await GitAsync(["branch", "-m", "main", "trunk"]);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["baseline", "suppressions"], string.Empty, repository.FullName);

        Assert.Equal(ExitCode.Error, exitCode);
        Assert.Contains("merge base with 'main'", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repository.FullName, ".lockflight", "baselines", AcceptedSuppressions.FileName)));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(repository);

    private async Task InitRepositoryAsync()
    {
        await GitAsync(["init", "-q", "-b", "main"]);
        Write("lockflight.yaml", "version: 1\n");
        Write(".gitignore", ".lockflight/\n");
        Write("src/Order.cs", "class Order { }\n");
        await CommitAsync("init");
    }

    private async Task CommitAsync(string message)
    {
        await GitAsync(["add", "."]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", message]);
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
