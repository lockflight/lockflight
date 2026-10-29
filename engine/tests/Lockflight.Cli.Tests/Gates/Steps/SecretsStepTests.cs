using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates.Steps;

// These run gitleaks itself (LOCKFLIGHT_GITLEAKS or PATH; CI installs the pinned release on every runner) and skip where it is absent.
public sealed class SecretsStepTests : IDisposable
{
    // A Slack bot token shape gitleaks' default rules recognise; not a real token, and split so this file itself does not match.
    private const string FakeTokenValue = "xo" + "xb-123456789012-1234567890123-AbCdEfGhIjKlMnOpQrStUvWx";

    private const string FakeToken = "slack = \"" + FakeTokenValue + "\"\n";

    private const string RedGreenEvidence = ".lockflight/features/001-shop/redgreen.json";

    // Two SHA-256 hex digests, the form the engine writes for test files and approved artifacts.
    private const string Sha256 = "a466cc5558316d8973d54a75b9fa7946db9ff2ac4caf1c61641ff8c9b915ca40";

    private const string OtherSha256 = "67487c4a60afe540134899bf8af023a3275d23b813444ee8b6ce2b032a9fdd13";

    private const string HouseRule = "[[rules]]\nid = \"house-secret\"\nregex = 'HOUSE[0-9]{8}'\n";

    private const string HouseSecret = "value HOUSE12345678\n";

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

    [Fact]
    public async Task TestFileHashesInRedGreenEvidenceDoNotFailTheStep()
    {
        await InitRepositoryAsync();
        Write(RedGreenEvidence, EvidenceWith($"\"tests/Shop.Tests/TokenTests.cs\": \"{Sha256}\""));

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Pass, step.Verdict);
    }

    [Fact]
    public async Task ChangedHashesInCommittedRedGreenEvidenceDoNotFailTheStep()
    {
        await InitRepositoryAsync();
        Write(RedGreenEvidence, EvidenceWith($"\"tests/Shop.Tests/ApiClientTests.cs\": \"{Sha256}\""));
        await CommitAsync();
        Write(RedGreenEvidence, EvidenceWith($"\"tests/Shop.Tests/ApiClientTests.cs\": \"{OtherSha256}\""));

        GateStep uncommitted = await RunStepAsync();
        await GitAsync(["add", "."]);
        GateStep staged = await RunStepAsync();

        Assert.Equal(Verdict.Pass, uncommitted.Verdict);
        Assert.Equal(Verdict.Pass, staged.Verdict);
    }

    [Fact]
    public async Task HashesInRedGreenEvidenceCommittedOnTheBranchDoNotFailTheStep()
    {
        await InitRepositoryAsync();
        await GitAsync(["branch", "base"]);
        Write(RedGreenEvidence, EvidenceWith($"\"tests/Shop.Tests/ApiClientTests.cs\": \"{Sha256}\""));
        await CommitAsync();

        GateStep step = await RunStepAsync(baseRef: "base");

        Assert.Equal(Verdict.Pass, step.Verdict);
    }

    [Fact]
    public async Task HashesInApprovalAndReviewEvidenceDoNotFailTheStep()
    {
        await InitRepositoryAsync();
        Write(".lockflight/features/001-shop/approvals/tasks.json", EvidenceWith($"\"contracts/auth-api.md\": \"{Sha256}\""));
        Write(".lockflight/features/001-shop/reviews/run-1.json", EvidenceWith($"\"tokenHead\": \"{Sha256[..40]}\""));

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Pass, step.Verdict);
    }

    [Fact]
    public async Task ASecretInRedGreenEvidenceStillFailsTheStep()
    {
        await InitRepositoryAsync();
        Write(RedGreenEvidence, EvidenceWith($"\"slack\": \"{FakeTokenValue}\""));

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains($"slack-bot-token at {RedGreenEvidence}", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGenericKeyThatIsNotAHashInRedGreenEvidenceStillFailsTheStep()
    {
        await InitRepositoryAsync();
        Write(RedGreenEvidence, EvidenceWith("\"api_token\": \"Zq8vR2mK7tLp4XwN9cJd3HfB6sYe1GuA\""));

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains($"generic-api-key at {RedGreenEvidence}", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHashOutsideTheEngineEvidenceIsStillReported()
    {
        await InitRepositoryAsync();
        Write("notes/hashes.json", EvidenceWith($"\"tests/Shop.Tests/TokenTests.cs\": \"{Sha256}\""));

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("generic-api-key at notes/hashes.json", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProjectsGitleaksConfigStillAppliesBesideTheEngineHashes()
    {
        await InitRepositoryAsync();
        Write(".gitleaks.toml", "[extend]\nuseDefault = true\n\n" + HouseRule);
        Write("notes/config.txt", HouseSecret);
        Write(RedGreenEvidence, EvidenceWith($"\"tests/Shop.Tests/TokenTests.cs\": \"{Sha256}\""));

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("house-secret at notes/config.txt:1", step.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("generic-api-key", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACommittedChangeIsScannedWithTheProjectsGitleaksConfig()
    {
        await InitRepositoryAsync();
        Write(".gitleaks.toml", HouseRule);
        await CommitAsync();
        Write("README.md", HouseSecret);

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("house-secret at README.md:1", step.Detail, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(repository);

    private async Task<GateStep> RunStepAsync(string? baseRef = null)
    {
        Assert.SkipWhen(
            Executables.Find("gitleaks", Environment.GetEnvironmentVariable(EnvironmentVariables.Gitleaks)) is null,
            "gitleaks is not installed (set LOCKFLIGHT_GITLEAKS or put it on PATH)");
        GateRunContext run = new(repository.FullName, repository.FullName, GateProfile.PullRequest, GateSettings.Default, baseRef, TimeProvider.System);
        return (await new SecretsStep().RunAsync(run, GateRunFacts.None, TestContext.Current.CancellationToken)).Step;
    }

    private async Task InitRepositoryAsync()
    {
        await GitAsync(["init", "-q"]);
        Write(".gitignore", "local/\n");
        Write("README.md", "tracked\n");
        await GitAsync(["add", "."]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "init"]);
    }

    private static string EvidenceWith(string entry) =>
        $"{{\n  \"schema\": \"lockflight.redgreen/v1\",\n  \"cycles\": [\n    {{\n      \"testFiles\": {{\n        {entry}\n      }}\n    }}\n  ]\n}}\n";

    private async Task CommitAsync()
    {
        await GitAsync(["add", "."]);
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
