using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Tests.Schemas;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// The CI action's artifact digests (<c>action/artifacts.sh</c>), run with bash as the action runs them: the judge and review
/// jobs output the digest of what they upload, and the report refuses an artifact that does not hash to it, then reads the
/// refusal as its verdict.
/// </summary>
public sealed class ArtifactDigestTests : IDisposable
{
    private static readonly string Head = new('a', 40);
    private static readonly string Script = Path.Combine(SchemaFiles.RepositoryRoot, "action", "artifacts.sh");

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-artifacts-");

    private string Evidence => Path.Combine(directory.FullName, "evidence");

    private string Review => Path.Combine(directory.FullName, "review");

    [Fact]
    public async Task TheDigestCoversEveryFileByPathAndContentButNotHiddenOnes()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The script is checked on Linux and macOS here; CI runs it on Windows in the action job.");
        Write(Evidence, "evidence.json", "{}");
        Write(Evidence, "gates-result.json", "{}");
        string digest = await DigestAsync(Evidence);

        Write(Evidence, ".hidden", "not uploaded");
        Assert.Equal(digest, await DigestAsync(Evidence));

        Write(Evidence, "gates-result.json", "{ }");
        Assert.NotEqual(digest, await DigestAsync(Evidence));

        File.Delete(Path.Combine(Evidence, "gates-result.json"));
        Write(Evidence, "other.json", "{}");
        Assert.NotEqual(digest, await DigestAsync(Evidence));
        Assert.Matches("^[0-9a-f]{64}$", digest);
    }

    [Fact]
    public async Task AMissingFolderHasTheDigestOfNothing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The script is checked on Linux and macOS here; CI runs it on Windows in the action job.");

        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", await DigestAsync(Path.Combine(directory.FullName, "none")));
    }

    [Fact]
    public async Task ArtifactsThatHashToTheirJobsDigestsAreLeftAsTheyAre()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The script is checked on Linux and macOS here; CI runs it on Windows in the action job.");
        Write(Evidence, "evidence.json", "{ \"judge\": true }");
        Write(Review, "review-result.json", "{ \"review\": true }");

        ProcessResult run = await VerifyAsync(await DigestAsync(Evidence), await DigestAsync(Review));

        Assert.True(run.ExitCode == 0, run.Error + run.Output);
        Assert.True(File.Exists(Path.Combine(Evidence, "evidence.json")));
        Assert.True(File.Exists(Path.Combine(Review, "review-result.json")));
        Assert.False(File.Exists(Path.Combine(Evidence, CiReportCommand.RefusalFileName)));
    }

    [Fact]
    public async Task ReplacedEvidenceIsRefusedAndTheReportFailsWithArtifactMismatch()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The script is checked on Linux and macOS here; CI runs it on Windows in the action job.");
        Write(Evidence, "evidence.json", "{ \"judge\": true }");
        string uploaded = await DigestAsync(Evidence);
        Write(Evidence, "evidence.json", "{ \"forged\": true }");

        ProcessResult run = await VerifyAsync(uploaded, string.Empty);

        Assert.True(run.ExitCode == 0, run.Error + run.Output);
        Assert.Contains("::error::the report refused an artifact: lockflight-evidence-x", run.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Evidence, "evidence.json")));
        (ExitCode exitCode, JsonObject verdict) = await ReportAsync();
        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Equal("refused", verdict["judge"]!.GetValue<string>());
        Assert.All(verdict["checks"]!.AsArray(), check =>
        {
            Assert.Equal("ARTIFACT_MISMATCH", check!["reason"]!.GetValue<string>());
            Assert.Contains($"not to the {uploaded[..12]}", check["why"]!.GetValue<string>(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task EvidenceGoneAfterItsJobNamedFilesIsAnError()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The script is checked on Linux and macOS here; CI runs it on Windows in the action job.");
        Write(Evidence, "evidence.json", "{ \"judge\": true }");
        string uploaded = await DigestAsync(Evidence);
        Directory.Delete(Evidence, recursive: true);

        await VerifyAsync(uploaded, string.Empty);

        (ExitCode exitCode, JsonObject verdict) = await ReportAsync();
        Assert.Equal(ExitCode.Error, exitCode);
        Assert.Equal("ARTIFACT_MISMATCH", verdict["checks"]![0]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task AReviewTheJudgeJobPlantedIsRefused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The script is checked on Linux and macOS here; CI runs it on Windows in the action job.");
        Write(Evidence, "evidence.json", "{ \"judge\": true }");
        Write(Review, "review-result.json", "{ \"review\": true }");
        string reviewed = await DigestAsync(Review);
        Write(Review, "review-result.json", "{ \"verdict\": \"PASS\" }");

        await VerifyAsync(await DigestAsync(Evidence), reviewed);

        (ExitCode exitCode, JsonObject verdict) = await ReportAsync();
        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("lockflight-review-x", verdict["checks"]![0]!["why"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Review));
    }

    [Fact]
    public async Task AReviewNoReviewJobVouchedForIsNotRead()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The script is checked on Linux and macOS here; CI runs it on Windows in the action job.");
        Write(Evidence, "evidence.json", "{ \"judge\": true }");
        Write(Review, "review-result.json", "{ \"verdict\": \"PASS\" }");

        ProcessResult run = await VerifyAsync(await DigestAsync(Evidence), string.Empty);

        Assert.True(run.ExitCode == 0, run.Error + run.Output);
        Assert.False(Directory.Exists(Review));
        Assert.True(File.Exists(Path.Combine(Evidence, "evidence.json")));
    }

    [Fact]
    public async Task EvidenceNoJudgeJobVouchedForIsNotRead()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The script is checked on Linux and macOS here; CI runs it on Windows in the action job.");
        Write(Evidence, "evidence.json", "{ \"judge\": true }");

        await VerifyAsync(string.Empty, string.Empty);

        Assert.False(Directory.Exists(Evidence));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(directory);

    private static void Write(string folder, string name, string content)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), content);
    }

    private async Task<string> DigestAsync(string folder)
    {
        ProcessResult run = await ProcessRunner.RunAsync("bash", [Script, "digest", folder], directory.FullName, TestContext.Current.CancellationToken);
        Assert.True(run.ExitCode == 0, run.Error);
        return run.Output.Trim();
    }

    private async Task<ProcessResult> VerifyAsync(string evidenceDigest, string reviewDigest) =>
        await ProcessRunner.RunAsync("bash", [Script, "verify", Evidence, evidenceDigest, Review, reviewDigest, Head, "-x"], directory.FullName, TestContext.Current.CancellationToken);

    private async Task<(ExitCode ExitCode, JsonObject Verdict)> ReportAsync()
    {
        string output = Path.Combine(directory.FullName, "out");
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["ci", "report", "--evidence", Evidence, "--review", Review, "--head", Head, "--out", output, "--context", "gates"], string.Empty, directory.FullName);
        return (exitCode, JsonNode.Parse(File.ReadAllText(Path.Combine(output, "verdict.json")))!.AsObject());
    }
}
