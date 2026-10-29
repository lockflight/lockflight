using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary><c>lockflight ci</c>'s inputs: full commit ids this clone has, the flags each platform needs, local defaults.</summary>
public sealed class JudgeInputsReaderTests : IAsyncLifetime
{
    private GitHistoryFixture repository = null!;
    private string main = string.Empty;
    private string head = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await repository.Write("README.md", "# shop\n").Write(".gitignore", ".lockflight/local/\n").CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("012-checkout", cancellationToken);
        head = await repository.Write("a.cs", "a").CommitAsync("feat: a", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("abc1234")]
    [InlineData("HEAD")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF01")]
    public async Task ACommitThatIsNotAFullLowerCaseIdIsRefused(string value)
    {
        (ExitCode exitCode, _, string error) = await RunAsync(["--platform", "github", "--head", value, "--base", main, "--evidence", Out]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--head must be a full commit id", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AValueStartingWithADashIsNotTakenAsACommit()
    {
        (ExitCode exitCode, _, string error) = await RunAsync(["--platform", "github", "--head", "--upload-pack=x", "--base", main]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--head needs a value", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACommitThisCloneDoesNotHaveIsRefusedWithTheFetchToAdd()
    {
        (ExitCode exitCode, _, string error) = await RunAsync(["--platform", "github", "--head", head, "--base", new string('e', 40), "--evidence", Out]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("is not a commit in this clone", error, StringComparison.Ordinal);
        Assert.Contains("fetch-depth: 0", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--head")]
    [InlineData("--base")]
    [InlineData("--evidence")]
    public async Task OnAPlatformTheHeadBaseAndEvidenceDirectoryAreRequired(string option)
    {
        List<string> args = ["--platform", "github", "--head", head, "--base", main, "--evidence", Out];
        args.RemoveRange(args.IndexOf(option), 2);

        (ExitCode exitCode, _, string error) = await RunAsync([.. args]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains($"{option} ", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownPlatformOrEventIsRefused()
    {
        Assert.Equal(ExitCode.Config, (await RunAsync(["--platform", "jenkins"])).ExitCode);
        Assert.Equal(ExitCode.Config, (await RunAsync(["--event", "tag"])).ExitCode);
    }

    [Fact]
    public async Task APushWithoutAPreviousCommitRunsWithoutABase()
    {
        (ExitCode exitCode, string output, _) = await RunAsync(["--platform", "github", "--event", "push", "--head", head, "--base", new string('0', 40), "--evidence", Out, "--plan"]);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("\"mergeBase\": null", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocallyTheHeadIsHeadAndTheBaseIsTheTargetBranch()
    {
        (ExitCode exitCode, string output, _) = await RunAsync(["--plan"]);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains($"\"head\": \"{head}\"", output, StringComparison.Ordinal);
        Assert.Contains($"\"base\": \"{main}\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocallyUncommittedChangesAreRefused()
    {
        repository.Write("a.cs", "changed");

        (ExitCode exitCode, _, string error) = await RunAsync(["--plan"]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("uncommitted changes (a.cs)", error, StringComparison.Ordinal);
    }

    private static string Out => Path.Combine(Path.GetTempPath(), "lockflight-ci-evidence-unused");

    private Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string[] args) => TestConsole.RunAsync(["ci", .. args], string.Empty, repository.Root);
}
