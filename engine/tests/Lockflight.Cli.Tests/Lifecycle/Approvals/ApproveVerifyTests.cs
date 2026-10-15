using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Lifecycle.Approvals;

public sealed class ApproveVerifyTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-verify-");

    public ApproveVerifyTests() => File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

    [Theory]
    [InlineData("bogus")]
    [InlineData("spec,")]
    [InlineData(",")]
    [InlineData("spec,tsks")]
    public async Task ARequireValueThatIsNotAListOfPhasesIsAUsageError(string require)
    {
        ApproveFeature("f1");

        (ExitCode exitCode, string output, string error) = await VerifyAsync(["--feature", "f1", "--require", require]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("which is not a phase", error, StringComparison.Ordinal);
        Assert.DoesNotContain("PASS", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyRequireValueIsAUsageError()
    {
        (ExitCode exitCode, _, string error) = await VerifyAsync(["--feature", "f1", "--require", ""]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--require needs a value", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequiredPhasesAreReadIgnoringCase()
    {
        ApproveFeature("f1");

        (ExitCode exitCode, string output, _) = await VerifyAsync(["--feature", "f1", "--require", "SPEC, Tasks"]);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("PASS f1: spec approved", output, StringComparison.Ordinal);
        Assert.Contains("PASS f1: tasks approved", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--base", "-c")]
    [InlineData("--base", "--upload-pack=x")]
    public async Task ABaseThatLooksLikeAnOptionIsRefused(params string[] args)
    {
        (ExitCode exitCode, _, string error) = await VerifyAsync(args);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--base needs a value", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeatureAndABaseTogetherAreRefused()
    {
        (ExitCode exitCode, _, string error) = await VerifyAsync(["--feature", "f1", "--base", "main"]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("exactly one of", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnApprovalCopiedFromAnotherFeatureDoesNotApproveThisOne()
    {
        ApproveFeature("f1");
        WriteSpecAndTasks("f2");
        string from = Path.Combine(root.FullName, ".lockflight", "features", "f1", "approvals");
        string to = Path.Combine(root.FullName, ".lockflight", "features", "f2", "approvals");
        Directory.CreateDirectory(to);
        foreach (string record in Directory.GetFiles(from))
        {
            File.Copy(record, Path.Combine(to, Path.GetFileName(record)));
        }

        (ExitCode exitCode, string output, _) = await VerifyAsync(["--feature", "f2"]);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("FAIL f2: spec approval record is not valid here: it was recorded for feature 'f1', not 'f2'", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeatureWithANonAsciiNameIsFoundAmongTheChangesSinceTheBase()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await GitAsync(["init", "-q", "-b", "main"], cancellationToken);
        await GitAsync(["config", "core.quotePath", "true"], cancellationToken);
        await CommitAllAsync(cancellationToken);
        await GitAsync(["checkout", "-qb", "feature"], cancellationToken);
        WriteSpecAndTasks("001-café");
        await CommitAllAsync(cancellationToken);

        (ExitCode exitCode, string output, _) = await VerifyAsync(["--base", "main"]);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("FAIL 001-café: spec is not approved", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARenamedFeatureIsVerifiedUnderItsNewNameAndTheOldNameIsNotAFailure()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await GitAsync(["init", "-q", "-b", "main"], cancellationToken);
        ApproveFeature("001-a");
        await CommitAllAsync(cancellationToken);
        await GitAsync(["checkout", "-qb", "feature"], cancellationToken);
        await GitAsync(["mv", "specs/001-a", "specs/003-b"], cancellationToken);
        ApproveFeature("003-b");
        await CommitAllAsync(cancellationToken);

        (ExitCode exitCode, string output, _) = await VerifyAsync(["--base", "main"]);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("NOTE 001-a: removed", output, StringComparison.Ordinal);
        Assert.Contains("PASS 003-b: spec approved", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANamedFeatureThatDoesNotExistStillFails()
    {
        (ExitCode exitCode, string output, _) = await VerifyAsync(["--feature", "no-such"]);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("FAIL no-such", output, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private Task<(ExitCode ExitCode, string Output, string Error)> VerifyAsync(string[] options) =>
        TestConsole.RunAsync(["approve", "verify", .. options], "", root.FullName);

    private Feature WriteSpecAndTasks(string id)
    {
        Feature feature = new(id, Path.Combine(root.FullName, "specs", id), Path.Combine(root.FullName, ".lockflight", "features", id));
        Directory.CreateDirectory(feature.SpecDirectory);
        File.WriteAllText(feature.SpecPath, "- **FR-001**: A\n");
        File.WriteAllText(feature.TasksPath, "- [ ] T001 [US1] (FR-001) Test A\n");
        return feature;
    }

    private void ApproveFeature(string id)
    {
        ApprovalStore approvals = new(WriteSpecAndTasks(id));
        approvals.Save(ApprovalPhase.Spec, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
        approvals.Save(ApprovalPhase.Tasks, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
    }

    private async Task CommitAllAsync(CancellationToken cancellationToken)
    {
        await GitAsync(["add", "-A"], cancellationToken);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "change", "--allow-empty"], cancellationToken);
    }

    private Task<ProcessResult> GitAsync(string[] arguments, CancellationToken cancellationToken) =>
        ProcessRunner.RunAsync("git", arguments, root.FullName, cancellationToken);
}
