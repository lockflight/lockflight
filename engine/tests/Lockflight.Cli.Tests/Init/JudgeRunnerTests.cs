using Lockflight.Cli.Init;
using Lockflight.Cli.Tests.Projects;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Tests.Init;

public sealed class JudgeRunnerTests : IDisposable
{
    private static readonly string[] OneChannel = [DotnetChannels.EngineChannel];

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-runner-");

    [Fact]
    public void AServerOnlySolutionGetsTheWorkflowAsTheTemplateWritesIt()
    {
        DesktopProjects.WriteServerTests(root.FullName);

        string workflow = GitHubWorkflow();

        Assert.Equal(Templates.Workflow(OneChannel, CiTrigger.PullRequest, developmentPin: false), workflow);
        Assert.All(Jobs(workflow), job => Assert.Equal(JudgeRunner.LinuxLabel, job.RunsOn));
    }

    [Fact]
    public void ADesktopSolutionRunsTheBuildingJobsOnWindowsInBashAndTheReportJobsOnLinux()
    {
        DesktopProjects.WriteWpfTests(root.FullName);
        DesktopProjects.WriteServerTests(root.FullName);

        string workflow = GitHubWorkflow();

        Dictionary<string, (string RunsOn, string? Shell)> jobs = Jobs(workflow).ToDictionary(job => job.Name, job => (job.RunsOn, job.Shell));
        Assert.Equal((JudgeRunner.WindowsLabel, "bash"), jobs["judge"]);
        Assert.Equal((JudgeRunner.WindowsLabel, "bash"), jobs["review"]);
        Assert.Equal((JudgeRunner.LinuxLabel, null), jobs["gates"]);
        Assert.Equal((JudgeRunner.LinuxLabel, null), jobs["judge-report"]);
    }

    [Fact]
    public void MovingToWindowsAndBackGivesTheTemplateAgainAndEachDirectionIsIdempotent()
    {
        string linux = Templates.Workflow(OneChannel);

        string windows = JudgeRunner.Apply(linux, windows: true);

        Assert.NotEqual(linux, windows);
        Assert.Equal(windows, JudgeRunner.Apply(windows, windows: true));
        Assert.Equal(linux, JudgeRunner.Apply(windows, windows: false));
        Assert.Equal(linux, JudgeRunner.Apply(linux, windows: false));
        Assert.Equal(JudgeRunner.WindowsLabel, JudgeRunner.RunsOn(windows, "judge"));
    }

    [Fact]
    public void ARunnerChosenByHandIsLeftAsItIsInBothDirections()
    {
        string chosen = Templates.Workflow(OneChannel).Replace(
            "  judge:\n    runs-on: ubuntu-latest", "  judge:\n    runs-on: [self-hosted, linux, big]", StringComparison.Ordinal);
        string windowsByHand = Templates.Workflow(OneChannel).Replace(
            "  judge:\n    runs-on: ubuntu-latest", "  judge:\n    runs-on: windows-2025", StringComparison.Ordinal);

        Assert.Equal("[self-hosted, linux, big]", JudgeRunner.RunsOn(JudgeRunner.Apply(chosen, windows: true), "judge"));
        Assert.Equal(JudgeRunner.WindowsLabel, JudgeRunner.RunsOn(JudgeRunner.Apply(chosen, windows: true), "review"));
        Assert.Equal("windows-2025", JudgeRunner.RunsOn(JudgeRunner.Apply(windowsByHand, windows: false), "judge"));
    }

    [Fact]
    public void AWorkflowWithWindowsLineEndingsKeepsThem()
    {
        string crlf = Templates.Workflow(OneChannel).ReplaceLineEndings("\r\n");

        string windows = JudgeRunner.Apply(crlf, windows: true);

        Assert.DoesNotContain("\n", windows.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("    runs-on: windows-latest\r\n    defaults:\r\n      run:\r\n        shell: bash\r\n", windows, StringComparison.Ordinal);
        Assert.Equal(crlf, JudgeRunner.Apply(windows, windows: false));
    }

    [Fact]
    public void AJobThatAlreadyHasDefaultsGetsNoSecondBlock()
    {
        string withDefaults = Templates.Workflow(OneChannel).Replace(
            "  judge:\n    runs-on: ubuntu-latest\n", "  judge:\n    runs-on: ubuntu-latest\n    defaults:\n      run:\n        working-directory: app\n", StringComparison.Ordinal);

        string windows = JudgeRunner.Apply(withDefaults, windows: true);

        Assert.Equal(JudgeRunner.WindowsLabel, JudgeRunner.RunsOn(windows, "judge"));
        Assert.Contains("    runs-on: windows-latest\n    defaults:\n      run:\n        working-directory: app\n", windows, StringComparison.Ordinal);
    }

    [Fact]
    public void InitExplainsTheRunnerAndItsCostOnGitHubAndAsksForAWindowsAgentElsewhere()
    {
        DesktopProjects.WriteWpfTests(root.FullName);
        IReadOnlyList<string> desktop = [Path.Combine(root.FullName, DesktopProjects.WpfTests.Replace('/', Path.DirectorySeparatorChar))];

        string gitHub = Assert.Single(CiPipeline.RunnerLines(CiPlatform.GitHub, desktop, root.FullName));
        string azure = Assert.Single(CiPipeline.RunnerLines(CiPlatform.Azure, desktop, root.FullName));

        Assert.Contains($"{DesktopProjects.WpfTests} targets Windows", gitHub, StringComparison.Ordinal);
        Assert.Contains("windows-latest", gitHub, StringComparison.Ordinal);
        Assert.Contains("twice the Linux rate on private repositories", gitHub, StringComparison.Ordinal);
        Assert.Contains("Windows agent", azure, StringComparison.Ordinal);
        Assert.Contains("NEEDS_WINDOWS_RUNNER", azure, StringComparison.Ordinal);
        Assert.Empty(CiPipeline.RunnerLines(CiPlatform.GitHub, [], root.FullName));
    }

    [Fact]
    public void TheOtherPlatformsTemplatesDoNotChangeForADesktopSolution()
    {
        string[] before = [.. CiPipeline.Files(CiPlatform.Azure, root.FullName, OneChannel, CiTrigger.PullRequest, developmentPin: false).Select(file => file.Content)];
        DesktopProjects.WriteWpfTests(root.FullName);

        string[] after = [.. CiPipeline.Files(CiPlatform.Azure, root.FullName, OneChannel, CiTrigger.PullRequest, developmentPin: false).Select(file => file.Content)];

        Assert.Equal(before, after);
    }

    private string GitHubWorkflow() =>
        Assert.Single(CiPipeline.Files(CiPlatform.GitHub, root.FullName, OneChannel, CiTrigger.PullRequest, developmentPin: false)).Content;

    private static IEnumerable<(string Name, string RunsOn, string? Shell)> Jobs(string workflow)
    {
        YamlStream yaml = [];
        yaml.Load(new StringReader(workflow));
        YamlMappingNode jobs = (YamlMappingNode)((YamlMappingNode)yaml.Documents[0].RootNode)["jobs"];
        foreach ((YamlNode name, YamlNode job) in jobs.Children)
        {
            YamlMappingNode mapping = (YamlMappingNode)job;
            string? shell = mapping.Children.TryGetValue("defaults", out YamlNode? defaults) ? ((YamlScalarNode)((YamlMappingNode)((YamlMappingNode)defaults)["run"])["shell"]).Value : null;
            yield return (((YamlScalarNode)name).Value!, ((YamlScalarNode)mapping["runs-on"]).Value!, shell);
        }
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
