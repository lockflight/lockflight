using Lockflight.Cli.Init;
using Lockflight.Cli.Projects;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// Whether the CI judge runs where the test projects can run. A test project that needs Windows (a <c>net*-windows</c>
/// framework, WPF or Windows Forms) cannot run on a Linux runner, where the judge reports NEEDS_WINDOWS_RUNNER; a judge on
/// <c>windows-latest</c> for a solution that does not need it costs Windows minutes for nothing. Another platform's template
/// runs on a Linux image, so a desktop solution needs a Windows agent there. Empty when there is nothing to say.
/// </summary>
internal static class WorkflowRunnerCheck
{
    private const string Name = "workflow: judge runner";
    private const string UpdateFix = "a person runs `lockflight update --project`, which moves the judge and review jobs between ubuntu-latest and windows-latest (a runner chosen by hand is left as it is)";

    private static readonly string[] OtherPlatformFiles = [AzurePipelinesTemplate.Path, GitLabCiTemplate.Path, BitbucketPipelinesTemplate.Path];

    public static IReadOnlyList<DoctorCheck> Check(string root)
    {
        IReadOnlyList<string> windowsTests = ProjectFiles.WindowsTestProjects(root);
        List<DoctorCheck> checks = [];
        if (GitHubCheck(root, windowsTests) is DoctorCheck gitHub)
        {
            checks.Add(gitHub);
        }

        if (windowsTests.Count > 0)
        {
            checks.AddRange(OtherPlatformFiles
                .Where(file => File.Exists(Path.Combine(root, file)) && File.ReadAllText(Path.Combine(root, file)).Contains("lockflight", StringComparison.Ordinal))
                .Select(file => new DoctorCheck(Name, CheckStatus.Warn,
                    $"{Describe(root, windowsTests)}, and {file} runs the Lockflight job on a Linux image, where the judge reports NEEDS_WINDOWS_RUNNER",
                    "run the Lockflight job on a Windows agent (the template's steps run in bash: Git Bash on Windows)")));
        }

        return checks;
    }

    private static DoctorCheck? GitHubCheck(string root, IReadOnlyList<string> windowsTests)
    {
        string path = Path.Combine(root, Templates.WorkflowPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path) || File.ReadAllText(path) is not string workflow || !workflow.Contains("mode: judge", StringComparison.Ordinal)
            || JudgeRunner.RunsOn(workflow, "judge") is not string label)
        {
            return null;
        }

        bool onWindows = JudgeRunner.IsWindows(label);
        return windowsTests.Count switch
        {
            > 0 when !onWindows => new DoctorCheck(Name, CheckStatus.Warn,
                $"{Describe(root, windowsTests)}, and the judge job of {Templates.WorkflowPath} runs on '{label}': the judge reports NEEDS_WINDOWS_RUNNER there instead of a verdict",
                label == JudgeRunner.LinuxLabel ? UpdateFix : "change runs-on of the judge and review jobs to a Windows runner"),
            > 0 => new DoctorCheck(Name, CheckStatus.Pass, $"{Describe(root, windowsTests)}, and the judge job runs on '{label}'", null),
            _ when label == JudgeRunner.WindowsLabel => new DoctorCheck(Name, CheckStatus.Warn,
                $"no test project needs Windows, and the judge job of {Templates.WorkflowPath} runs on {JudgeRunner.WindowsLabel}: a Windows run takes longer, and private repositories pay twice the Linux rate for its minutes",
                UpdateFix + "; to keep Windows on purpose, name another Windows label such as windows-2025"),
            _ => null,
        };
    }

    private static string Describe(string root, IReadOnlyList<string> windowsTests) =>
        $"{string.Join(", ", windowsTests.Select(project => Path.GetRelativePath(root, project).Replace('\\', '/')))} {(windowsTests.Count == 1 ? "targets" : "target")} Windows";
}
