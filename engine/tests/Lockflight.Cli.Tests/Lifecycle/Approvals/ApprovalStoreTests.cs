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
    [InlineData("- [x] T001 Setup\n- [X] T002 [P] [US1] (FR-001) Build the parser\n")]
    [InlineData("- [x] T001 Setup\r\n- [ ] T002 [P] [US1] (FR-001) Build the parser\r\n")]
    public void TickingTasksKeepsTheTasksApprovalValid(string ticked)
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.TasksPath, "- [ ] T001 Setup\n- [ ] T002 [P] [US1] (FR-001) Build the parser\n");
        File.WriteAllText(Feature.PlanPath, "# Plan\n");
        ApprovalStore store = new(Feature);
        store.Save(ApprovalPhase.Tasks, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");

        File.WriteAllText(Feature.TasksPath, ticked);

        Assert.Equal(ApprovalState.Valid, store.Check(ApprovalPhase.Tasks).State);
    }

    [Theory]
    [InlineData("- [ ] T001 Setup\n", "- [x] T001 Set up the project\n")]
    [InlineData("- [ ] T001 Setup\n", "- [x] T001 Setup\n- [ ] T002 Added later\n")]
    [InlineData("- [ ] Setup\n", "- [x] Setup\n")]
    [InlineData("- [ ] T001 Setup\n- [ ] check the release notes\n", "- [x] T001 Setup\n- [x] check the release notes\n")]
    public void ATickNextToAnyOtherChangeOrOffATaskLineStillMakesItStale(string approved, string edited)
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.TasksPath, approved);
        ApprovalStore store = new(Feature);
        store.Save(ApprovalPhase.Tasks, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");

        File.WriteAllText(Feature.TasksPath, edited);

        Assert.Equal(["tasks.md"], store.Check(ApprovalPhase.Tasks).ChangedArtifacts);
    }

    [Fact]
    public void ATasksApprovalRecordedWithTicksAsWrittenStaysValid()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        const string Ticked = "- [x] T001 Setup\n- [ ] T002 Build\n";
        File.WriteAllText(Feature.TasksPath, Ticked);
        File.WriteAllText(Feature.PlanPath, "# Plan\n");
        ApprovalStore store = new(Feature);
        string plan = ArtifactHash.Of(Feature.PlanPath);
        store.Save(ApprovalPhase.Tasks, [("tasks.md", ArtifactHash.Of(Feature.TasksPath)), ("plan.md", plan)], "dev@example.com", DateTimeOffset.UnixEpoch, "0.3.0");

        Assert.Equal(ApprovalState.Valid, store.Check(ApprovalPhase.Tasks).State);
    }

    [Fact]
    public void TicksInTheSpecStillChangeItsApproval()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, "# Spec\n- [ ] T001 looks like a task\n");
        ApprovalStore store = new(Feature);
        store.Save(ApprovalPhase.Spec, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");

        File.WriteAllText(Feature.SpecPath, "# Spec\n- [x] T001 looks like a task\n");

        Assert.Equal(ApprovalState.Stale, store.Check(ApprovalPhase.Spec).State);
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
