using Lockflight.Cli.Init;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Init;

/// <summary>
/// The Bitbucket report steps' check for evidence the pull request commits, run as the step runs it (bash), after the base is
/// checked out as the step does.
/// </summary>
public sealed class CommittedEvidenceCheckTests : IDisposable
{
    private readonly DirectoryInfo repository = Directory.CreateTempSubdirectory("lockflight-committed-evidence-");

    public void Dispose() => TempDirectories.DeleteRecursively(repository);

    [Fact]
    public async Task EvidenceThePullRequestCommitsStopsTheReportAfterTheBaseIsCheckedOut()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The report steps run on Linux.");
        string head = await CommitsAsync(Path.Combine(CiShellScripts.EvidenceDirectory, "evidence.json"));

        ProcessResult check = await CheckAsync(head);

        Assert.Equal(4, check.ExitCode);
        Assert.Contains("the pull request commits files under", check.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APullRequestWithoutEvidenceFilesPasses()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The report steps run on Linux.");
        string head = await CommitsAsync(Path.Combine("src", "Orders.cs"));

        ProcessResult check = await CheckAsync(head);

        Assert.True(check.ExitCode == 0, check.Error + check.Output);
    }

    /// <summary>A base commit, then a pull request commit adding <paramref name="file"/>; the base is checked out again.</summary>
    private async Task<string> CommitsAsync(string file)
    {
        await GitAsync("init", "-q", "-b", "main");
        await GitAsync("-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-q", "--allow-empty", "-m", "base");
        string baseCommit = (await GitAsync("rev-parse", "HEAD")).Output.Trim();
        string path = Path.Combine(repository.FullName, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}\n");
        await GitAsync("add", "-A");
        await GitAsync("-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "pull request");
        string head = (await GitAsync("rev-parse", "HEAD")).Output.Trim();
        await GitAsync("checkout", "--detach", "--quiet", baseCommit);
        return head;
    }

    private async Task<ProcessResult> CheckAsync(string head)
    {
        string step = Path.Combine(repository.FullName, ".git", "check.sh");
        File.WriteAllText(step, CiShellScripts.CommittedEvidenceCheck + "\n");
        return await ProcessRunner.RunAsync("env", [$"HEAD_COMMIT={head}", "bash", "-e", step], repository.FullName, TestContext.Current.CancellationToken);
    }

    private async Task<ProcessResult> GitAsync(params string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, repository.FullName, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Error);
        return result;
    }
}
