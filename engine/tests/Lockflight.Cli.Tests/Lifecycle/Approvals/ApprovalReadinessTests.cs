using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Lifecycle.Approvals;

public sealed class ApprovalReadinessTests : IDisposable
{
    private const string FeatureId = "001-orders";
    private const string SpecWithoutScenarios = "# Orders\n\n### User Story 1 - Place an order\n\n- **FR-001**: Customers place orders\n";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-approval-readiness-");

    private string SpecDirectory => Path.Combine(root.FullName, "specs", FeatureId);

    private string RecordPath(string phase) => Path.Combine(root.FullName, ".lockflight", "features", FeatureId, "approvals", $"{phase}.json");

    [Fact]
    public async Task ASpecThatFailsItsPhaseCheckIsNotApprovedInATerminal()
    {
        await CreateProjectAsync(SpecWithoutScenarios);

        (ExitCode exitCode, string output, string error) = await ApproveAsync("spec");

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Empty(output);
        Assert.StartsWith($"lockflight: nothing was approved: the spec of {FeatureId} does not pass the phase check an approval needs (specify):", error, StringComparison.Ordinal);
        Assert.Contains("  FAIL spec.stories: no Given/When/Then scenario in: User Story 1\n", error, StringComparison.Ordinal);
        Assert.Contains("rewrite it to Spec Kit's template (.specify/templates/spec-template.md)", error, StringComparison.Ordinal);
        Assert.Contains($"phase check specify --feature {FeatureId}` until it passes, and approve again.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("predates Lockflight", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
        Assert.False(Directory.Exists(Path.Combine(root.FullName, ".lockflight", "ledger")));
    }

    [Fact]
    public async Task ASpecThatPassesItsPhaseCheckIsApprovedInATerminal()
    {
        await CreateProjectAsync(ApprovableArtifacts.Spec);

        (ExitCode exitCode, string output, _) = await ApproveAsync("spec");

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains($"Approved spec of {FeatureId}", output, StringComparison.Ordinal);
        Assert.True(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task TasksAreApprovedInATerminalOnlyAfterTheSpec()
    {
        await CreateProjectAsync(ApprovableArtifacts.Spec);
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "tasks.md"), ApprovableArtifacts.Tasks, TestContext.Current.CancellationToken);

        (ExitCode beforeExit, _, string beforeError) = await ApproveAsync("tasks");

        Assert.Equal(ExitCode.Fail, beforeExit);
        Assert.Contains("(tasks):\n  FAIL spec.approved: spec is not approved", beforeError, StringComparison.Ordinal);
        Assert.Contains("approve the spec first when it is not approved", beforeError, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("tasks")));

        await ApproveAsync("spec");
        (ExitCode afterExit, _, _) = await ApproveAsync("tasks");

        Assert.Equal(ExitCode.Pass, afterExit);
        Assert.True(File.Exists(RecordPath("tasks")));
    }

    [Fact]
    public async Task TasksThatFailTheirPhaseCheckAreNotApprovedInATerminal()
    {
        await CreateProjectAsync(ApprovableArtifacts.Spec);
        await ApproveAsync("spec");
        await File.WriteAllTextAsync(
            Path.Combine(SpecDirectory, "tasks.md"), "- [ ] T001 [US1] (FR-001) Place the order\n", TestContext.Current.CancellationToken);

        (ExitCode exitCode, _, string error) = await ApproveAsync("tasks");

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("  FAIL tasks.cover-requirements: requirements without a task: FR-002\n", error, StringComparison.Ordinal);
        Assert.Contains("  FAIL tasks.tests-first: US1 has no test task", error, StringComparison.Ordinal);
        Assert.DoesNotContain("FAIL spec.approved", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("tasks")));
    }

    [Fact]
    public async Task AnAdoptedFeatureThatFailsItsPhaseCheckIsToldItNeedsNoApproval()
    {
        await CreateProjectAsync(SpecWithoutScenarios);
        new AdoptedFeatures(root.FullName).Save([FeatureId]);

        (ExitCode exitCode, _, string error) = await ApproveAsync("spec");

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("FAIL spec.stories", error, StringComparison.Ordinal);
        Assert.Contains($"{FeatureId} predates Lockflight (adopted): `approve verify --base` and `review` need no approval for it", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private async Task CreateProjectAsync(string spec)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ProcessRunner.RunAsync("git", ["init", "-q"], root.FullName, cancellationToken);
        await ProcessRunner.RunAsync("git", ["config", "user.email", "approver@example.com"], root.FullName, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n", cancellationToken);
        Directory.CreateDirectory(SpecDirectory);
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "spec.md"), spec, cancellationToken);
    }

    private Task<(ExitCode ExitCode, string Output, string Error)> ApproveAsync(string phase) =>
        TestConsole.RunAsync(["approve", phase, "--feature", FeatureId], string.Empty, root.FullName);
}
