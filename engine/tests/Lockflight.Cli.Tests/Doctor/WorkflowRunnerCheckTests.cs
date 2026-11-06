using Lockflight.Cli.Doctor;
using Lockflight.Cli.Init;
using Lockflight.Cli.Tests.Projects;

namespace Lockflight.Cli.Tests.Doctor;

public sealed class WorkflowRunnerCheckTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-runner-check-");

    [Fact]
    public void ADesktopTestProjectWithTheJudgeOnLinuxIsAWarningThatNamesUpdate()
    {
        DesktopProjects.WriteWpfTests(root.FullName);
        WriteWorkflow(Templates.Workflow(["10.0.x"]));

        DoctorCheck check = Assert.Single(WorkflowRunnerCheck.Check(root.FullName));

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("NEEDS_WINDOWS_RUNNER", check.Detail, StringComparison.Ordinal);
        Assert.Contains("lockflight update --project", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ADesktopTestProjectWithTheJudgeOnWindowsPasses()
    {
        DesktopProjects.WriteWpfTests(root.FullName);
        WriteWorkflow(JudgeRunner.Apply(Templates.Workflow(["10.0.x"]), windows: true));

        Assert.Equal(CheckStatus.Pass, Assert.Single(WorkflowRunnerCheck.Check(root.FullName)).Status);
    }

    [Fact]
    public void AServerOnlySolutionWithTheJudgeOnWindowsLatestIsAWarningAboutMinutes()
    {
        DesktopProjects.WriteServerTests(root.FullName);
        WriteWorkflow(JudgeRunner.Apply(Templates.Workflow(["10.0.x"]), windows: true));

        DoctorCheck check = Assert.Single(WorkflowRunnerCheck.Check(root.FullName));

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("twice the Linux rate", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AServerOnlySolutionOnLinuxOrOnAWindowsRunnerChosenByHandSaysNothing()
    {
        DesktopProjects.WriteServerTests(root.FullName);
        WriteWorkflow(Templates.Workflow(["10.0.x"]));
        Assert.Empty(WorkflowRunnerCheck.Check(root.FullName));

        WriteWorkflow(Templates.Workflow(["10.0.x"]).Replace("  judge:\n    runs-on: ubuntu-latest", "  judge:\n    runs-on: windows-2025", StringComparison.Ordinal));
        Assert.Empty(WorkflowRunnerCheck.Check(root.FullName));
    }

    [Fact]
    public void ADesktopTestProjectWithAnotherPlatformsLinuxImageIsAWarningForAWindowsAgent()
    {
        DesktopProjects.WriteWpfTests(root.FullName);
        string azure = Path.Combine(root.FullName, AzurePipelinesTemplate.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(azure)!);
        File.WriteAllText(azure, AzurePipelinesTemplate.Pipeline(["10.0.x"], CiTrigger.PullRequest, developmentPin: false));

        DoctorCheck check = Assert.Single(WorkflowRunnerCheck.Check(root.FullName));

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains(AzurePipelinesTemplate.Path, check.Detail, StringComparison.Ordinal);
        Assert.Contains("Windows agent", check.Fix, StringComparison.Ordinal);
    }

    private void WriteWorkflow(string text)
    {
        string path = Path.Combine(root.FullName, Templates.WorkflowPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
