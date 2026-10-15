using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class StaleApprovalTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-stale-");

    public StaleApprovalTests()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, "- **FR-001**: A\n");
        File.WriteAllText(Feature.TasksPath, "- [ ] T001 [US1] (FR-001) Test A\n");
        ApprovalStore approvals = new(Feature);
        approvals.Save(ApprovalPhase.Spec, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
        approvals.Save(ApprovalPhase.Tasks, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
    }

    private Feature Feature => new("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1"));

    [Fact]
    public async Task ARedIsRefusedWhenTheTasksChangedAfterApproval()
    {
        SaveRecord(openCycle: false);
        File.AppendAllText(Feature.TasksPath, "- [ ] T002 [US1] (FR-001) Sneaked in\n");

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["redgreen", "red", "--feature", "f1"], "", root.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("tasks approval is stale: tasks.md changed", error, StringComparison.Ordinal);
        Assert.Contains("in a separate terminal window", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGreenIsRefusedWhenTheSpecChangedAfterApproval()
    {
        SaveRecord(openCycle: true);
        File.AppendAllText(Feature.SpecPath, "- **FR-002**: B\n");

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["redgreen", "green", "--feature", "f1"], "", root.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("spec approval is stale: spec.md changed", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARedIsRefusedWhenTheSpecApprovalRecordIsDeleted()
    {
        SaveRecord(openCycle: false);
        File.Delete(new ApprovalStore(Feature).RecordPath(ApprovalPhase.Spec));

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["redgreen", "red", "--feature", "f1"], "", root.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("spec is not approved", error, StringComparison.Ordinal);
        Assert.Contains("in a separate terminal window", error, StringComparison.Ordinal);
    }

    public void Dispose() => root.Delete(recursive: true);

    private void SaveRecord(bool openCycle)
    {
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string> { "T:A.B" },
            TestProjects = ["tests/Shop.Tests"],
        };
        if (openCycle)
        {
            record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null, [new RedGreenTest("T:A.C", "A.C", RedClass.RedStub, ["FR-001"])]));
        }

        record.Save(RedGreenRecord.PathFor(Feature), Feature.Id);
    }
}
