using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Tests.Schemas;

namespace Lockflight.Cli.Tests.Ci.Explain;

public sealed class CiExplainCommandTests : IDisposable
{
    private static readonly string FixtureDirectory = Path.Combine(SchemaFiles.RepositoryRoot, "engine", "tests", "Lockflight.Cli.Tests", "Ci", "Explain", "Fixtures");

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-ci-explain-");

    private readonly List<IReadOnlyList<string>> ghCalls = [];

    [Fact]
    public async Task AnEvidenceFileIsExplainedWithItsReasonAndNextLine()
    {
        Copy("green-before", "evidence.json");

        (ExitCode exitCode, string output, _) = await RunAsync(["ci", "explain", "evidence.json"], Unused);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.StartsWith("FAIL lockflight ci for head aaaaaaa: the CI judge's evidence", output, StringComparison.Ordinal);
        Assert.Contains("Reason: GREEN_BEFORE", output, StringComparison.Ordinal);
        Assert.Contains("Source: evidence.json", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFolderIsReadForTheVerdictBeforeTheEvidence()
    {
        Copy("green-before", Path.Combine("artifacts", "lockflight-evidence", "evidence.json"));
        Copy("review-findings", Path.Combine("artifacts", "lockflight-verdict", "verdict.json"));

        (ExitCode exitCode, string output, _) = await RunAsync(["ci", "explain", "artifacts"], Unused);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("Source: artifacts/lockflight-verdict/verdict.json", output, StringComparison.Ordinal);
        Assert.Contains("REVIEW_FINDINGS", output, StringComparison.Ordinal);
        Assert.DoesNotContain("GREEN_BEFORE", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAnOperandTheLocalJudgesLastRunIsExplained()
    {
        Copy("local-pass", Path.Combine(".lockflight", "local", "ci", "last.json"));
        Directory.CreateDirectory(Path.Combine(directory.FullName, "src"));

        (ExitCode exitCode, string output, _) = await RunAsync(["ci", "explain"], GitAt(directory.FullName), Path.Combine(directory.FullName, "src"));

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("the local judge's evidence", output, StringComparison.Ordinal);
        Assert.Contains("Source: .lockflight/local/ci/last.json", output, StringComparison.Ordinal);
        Assert.Contains("Nothing to explain", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAnOperandOrALocalRunItSaysWhatToRun()
    {
        (ExitCode exitCode, _, string error) = await RunAsync(["ci", "explain"], GitAt(directory.FullName));

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("lockflight ci --platform local", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheJsonFlagPrintsTheExplanationsAsJson()
    {
        Copy("scanner-replaced", "evidence.json");

        (ExitCode exitCode, string output, _) = await RunAsync(["ci", "explain", "--json", "evidence.json"], Unused);

        Assert.Equal(ExitCode.Pass, exitCode);
        JsonNode gates = JsonNode.Parse(output)!["explanations"]![0]!["checks"]![0]!;
        Assert.Equal("SCANNER_REPLACED", gates["steps"]![0]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task ARunUrlIsDownloadedWithGhForItsRepository()
    {
        (ExitCode exitCode, string output, _) = await RunAsync(["ci", "explain", "https://github.com/acme/shop/actions/runs/42/job/7"], GhWrites("engine-lock-refused", "lockflight-evidence", "refused.json"));

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("ENGINE_LOCK_REFUSED", output, StringComparison.Ordinal);
        Assert.Contains("Source: run 42/lockflight-evidence/refused.json", output, StringComparison.Ordinal);
        IReadOnlyList<string> call = Assert.Single(ghCalls);
        Assert.Equal(["run", "download", "42"], call.Take(3));
        Assert.Equal("acme/shop", call[call.ToList().IndexOf("--repo") + 1]);
    }

    [Fact]
    public async Task ARunIdUsesTheRepositoryGhFindsAndTheDownloadIsRemoved()
    {
        string? downloaded = null;
        RunProcess gh = async (fileName, arguments, workingDirectory, cancellationToken) =>
        {
            downloaded = arguments[arguments.ToList().IndexOf("--dir") + 1];
            return await GhWrites("green-before", "lockflight-evidence", "evidence.json")(fileName, arguments, workingDirectory, cancellationToken);
        };

        (ExitCode exitCode, _, _) = await RunAsync(["ci", "explain", "42"], gh);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.DoesNotContain("--repo", Assert.Single(ghCalls));
        Assert.False(Directory.Exists(downloaded));
    }

    [Fact]
    public async Task WithoutGhItSaysToInstallIt()
    {
        RunProcess missing = (fileName, _, _, _) => throw new ExecutableNotFoundException(fileName, new FileNotFoundException());

        (ExitCode exitCode, _, string error) = await RunAsync(["ci", "explain", "42"], missing);

        Assert.Equal(ExitCode.Error, exitCode);
        Assert.Contains("GitHub CLI (gh) is not on PATH", error, StringComparison.Ordinal);
        Assert.Contains("gh auth login", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGhThatIsNotSignedInSaysToSignIn()
    {
        RunProcess signedOut = (_, _, _, _) => Task.FromResult(new ProcessResult(4, string.Empty, "To get started with GitHub CLI, please run:  gh auth login", 10, false));

        (ExitCode exitCode, _, string error) = await RunAsync(["ci", "explain", "42"], signedOut);

        Assert.Equal(ExitCode.Error, exitCode);
        Assert.Contains("not signed in", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARunWithoutLockflightArtifactsSaysSo()
    {
        RunProcess none = (_, _, _, _) => Task.FromResult(new ProcessResult(1, string.Empty, "no valid artifacts found to download", 10, false));

        (ExitCode exitCode, _, string error) = await RunAsync(["ci", "explain", "42"], none);

        Assert.Equal(ExitCode.Error, exitCode);
        Assert.Contains("has no lockflight-verdict or lockflight-evidence artifact", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nothing-here.json")]
    [InlineData("https://github.com/acme/shop/pull/3")]
    public async Task AnOperandThatIsNeitherAFileNorARunIsAConfigurationError(string operand)
    {
        (ExitCode exitCode, _, string error) = await RunAsync(["ci", "explain", operand], Unused);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("is not a file or folder here", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileThatIsNotLockflightEvidenceIsAConfigurationError()
    {
        File.WriteAllText(Path.Combine(directory.FullName, "other.json"), "{\"schema\":\"something/v1\"}");

        (ExitCode exitCode, _, string error) = await RunAsync(["ci", "explain", "other.json"], Unused);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("other.json was not explained", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoOperandsAreAConfigurationError()
    {
        (ExitCode exitCode, _, string error) = await RunAsync(["ci", "explain", "a.json", "b.json"], Unused);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("not 2", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownOptionIsAConfigurationError()
    {
        (ExitCode exitCode, _, string error) = await RunAsync(["ci", "explain", "--yaml"], Unused);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--json", error, StringComparison.Ordinal);
    }

    public void Dispose() => directory.Delete(recursive: true);

    private static Task<ProcessResult> Unused(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"{fileName} was not expected to run");

    private static RunProcess GitAt(string root) => (fileName, _, _, _) =>
        Task.FromResult(fileName == "git" ? new ProcessResult(0, root + "\n", string.Empty, 1, false) : throw new InvalidOperationException($"{fileName} was not expected to run"));

    /// <summary>A <c>gh</c> that records its call and writes a fixture into the artifact folder under <c>--dir</c>, as gh run download does.</summary>
    private RunProcess GhWrites(string fixture, string artifact, string fileName) => (program, arguments, _, _) =>
    {
        Assert.Equal("gh", program);
        ghCalls.Add(arguments);
        string target = Path.Combine(arguments[arguments.ToList().IndexOf("--dir") + 1], artifact);
        Directory.CreateDirectory(target);
        File.Copy(Path.Combine(FixtureDirectory, $"{fixture}.json"), Path.Combine(target, fileName));
        return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, 10, false));
    };

    private void Copy(string fixture, string relativePath)
    {
        string path = Path.Combine(directory.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(Path.Combine(FixtureDirectory, $"{fixture}.json"), path);
    }

    private Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string[] args, RunProcess processes) => RunAsync(args, processes, directory.FullName);

    private static async Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string[] args, RunProcess processes, string workingDirectory)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(string.Empty), output, error, workingDirectory, TimeProvider.System) { Processes = processes };

        ExitCode exitCode = await CliApplication.RunAsync(args, context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString().Trim(), error.ToString().Trim());
    }
}
