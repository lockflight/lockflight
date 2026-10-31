using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests.Lifecycle.Approvals;

public sealed class ApprovalStoreTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-approval-");

    private Feature Feature => new("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1"));

    [Fact]
    public void AnApprovalStaysValidAcrossLineEndingsAndGoesStaleOnEdits()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, "# Spec\n- **FR-001**: A\n");
        ApprovalStore store = new(Feature);
        Assert.Equal(ApprovalState.Missing, store.Check(ApprovalPhase.Spec).State);

        store.Save(ApprovalPhase.Spec, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
        File.WriteAllText(Feature.SpecPath, "﻿# Spec\r\n- **FR-001**: A\r\n");
        Assert.Equal(ApprovalState.Valid, store.Check(ApprovalPhase.Spec).State);

        File.AppendAllText(Feature.SpecPath, "- **FR-002**: B\n");
        ApprovalCheck stale = store.Check(ApprovalPhase.Spec);
        Assert.Equal(ApprovalState.Stale, stale.State);
        Assert.Equal(["spec.md"], stale.ChangedArtifacts);
    }

    [Fact]
    public void ATasksApprovalAlsoBindsThePlanEvenWhenItAppearsLater()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.TasksPath, "- [ ] T001 Setup\n");
        ApprovalStore store = new(Feature);
        store.Save(ApprovalPhase.Tasks, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");

        File.WriteAllText(Feature.PlanPath, "# Plan\n");

        Assert.Equal(["plan.md"], store.Check(ApprovalPhase.Tasks).ChangedArtifacts);
    }

    [Fact]
    public void ARecordSavedAsAnotherPhaseDoesNotApproveThisOne()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, "# Spec\n");
        ApprovalStore store = new(Feature);
        store.Save(ApprovalPhase.Spec, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
        File.Copy(store.RecordPath(ApprovalPhase.Spec), store.RecordPath(ApprovalPhase.Tasks));

        ApprovalCheck check = store.Check(ApprovalPhase.Tasks);

        Assert.Equal(ApprovalState.Invalid, check.State);
        Assert.Contains("recorded for phase 'spec', not 'tasks'", check.Describe(root.FullName), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("{\"feature\": 7, \"phase\": \"spec\", \"artifacts\": {}}")]
    public void AnUnreadableRecordIsInvalidRatherThanACrash(string content)
    {
        ApprovalStore store = new(Feature);
        Directory.CreateDirectory(Path.GetDirectoryName(store.RecordPath(ApprovalPhase.Spec))!);
        File.WriteAllText(store.RecordPath(ApprovalPhase.Spec), content);

        Assert.Equal(ApprovalState.Invalid, store.Check(ApprovalPhase.Spec).State);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
