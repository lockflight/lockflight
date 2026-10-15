using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates.Steps;

// These run gitleaks itself (LOCKFLIGHT_GITLEAKS or PATH; CI installs the pinned release on Linux) and skip where it is absent.
public sealed class SecretsStepTests : IDisposable
{
    // A Slack bot token shape gitleaks' default rules recognise; not a real token, and split so this file itself does not match.
    private const string FakeToken = "slack = \"xo" + "xb-123456789012-1234567890123-AbCdEfGhIjKlMnOpQrStUvWx\"\n";

    private readonly DirectoryInfo repository = Directory.CreateTempSubdirectory("lockflight-secrets-");

    [Fact]
    public async Task ASecretInAnUntrackedFileFailsTheStep()
    {
        await InitRepositoryAsync();
        Write("notes/new-config.txt", FakeToken);

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("slack-bot-token at notes/new-config.txt:1", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecretInAnIgnoredFileIsNotScanned()
    {
        await InitRepositoryAsync();
        Write("local/secrets.txt", FakeToken);

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Pass, step.Verdict);
        Assert.Contains("0 untracked files scanned", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACleanUntrackedFileIsScannedAndPasses()
    {
        await InitRepositoryAsync();
        Write("src/Plain.cs", "class Plain {}\n");

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Pass, step.Verdict);
        Assert.Contains("1 untracked files scanned", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInlineAllowanceInAnUntrackedFileFailsTheStep()
    {
        await InitRepositoryAsync();
        Write("notes/new-config.txt", FakeToken.TrimEnd('\n') + " # gitleaks:allow\n");

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("inline 'gitleaks:allow': notes/new-config.txt", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFindingAcceptedInGitleaksIgnoreIsNotReportedForAnUntrackedFile()
    {
        await InitRepositoryAsync();
        Write("notes/new-config.txt", FakeToken);
        Write(".gitleaksignore", "notes/new-config.txt:slack-bot-token:1\n");

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Pass, step.Verdict);
    }

    [Fact]
    public async Task BeforeTheFirstCommitAStagedInlineAllowanceStillFailsTheStep()
    {
        await GitAsync(["init", "-q"]);
        Write("notes/config.txt", "token = x # gitleaks:allow\n");
        await GitAsync(["add", "."]);

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("inline 'gitleaks:allow': notes/config.txt", step.Detail, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(repository);

    private async Task<GateStep> RunStepAsync()
    {
        Assert.SkipWhen(
            Executables.Find("gitleaks", Environment.GetEnvironmentVariable(EnvironmentVariables.Gitleaks)) is null,
            "gitleaks is not installed (set LOCKFLIGHT_GITLEAKS or put it on PATH)");
        GateRunContext run = new(repository.FullName, repository.FullName, GateProfile.PullRequest, GateSettings.Default, BaseRef: null, TimeProvider.System);
        return await SecretsStep.RunAsync(run, TestContext.Current.CancellationToken);
    }

    private async Task InitRepositoryAsync()
    {
        await GitAsync(["init", "-q"]);
        Write(".gitignore", "local/\n");
        Write("README.md", "tracked\n");
        await GitAsync(["add", "."]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "init"]);
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
