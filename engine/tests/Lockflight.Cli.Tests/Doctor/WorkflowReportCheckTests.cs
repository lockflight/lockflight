using Lockflight.Cli.Doctor;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Doctor;

public sealed class WorkflowReportCheckTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-workflow-report-");

    [Fact]
    public void TheWorkflowThisEngineWritesHasNothingToSay()
    {
        Write(Templates.Workflow(["10.0.x"]));

        Assert.Empty(WorkflowReportCheck.Check(root.FullName));
    }

    [Fact]
    public void ReportJobsThatReadArtifactsByNameAreAWarningNamingTheMigrationProposal()
    {
        Write(Templates.WorkflowOf(ReleasedJudgeWorkflows.All[1], ["10.0.x"], CiTrigger.PullRequest, developmentPin: false));

        DoctorCheck check = Assert.Single(WorkflowReportCheck.Check(root.FullName));

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("read the artifacts by name", check.Detail, StringComparison.Ordinal);
        Assert.Contains(".lockflight/local/migration/", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void APinNoLockflightEngineWritesIsAWarning()
    {
        string pin = new('e', 40);
        Write(Templates.Workflow(["10.0.x"]).Replace(Templates.ActionCommit, pin, StringComparison.Ordinal));

        DoctorCheck check = Assert.Single(WorkflowReportCheck.Check(root.FullName));

        Assert.Equal("workflow: action pin", check.Name);
        Assert.Contains(pin[..12], check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AReleasedPinIsNoWarning()
    {
        Write(Templates.Workflow(["10.0.x"]).Replace(Templates.ActionCommit, ReleasedActionCommits.All[^1], StringComparison.Ordinal));

        Assert.Empty(WorkflowReportCheck.Check(root.FullName));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private void Write(string workflow)
    {
        string path = Path.Combine(root.FullName, ".github", "workflows", "lockflight-gates.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, workflow);
    }
}
