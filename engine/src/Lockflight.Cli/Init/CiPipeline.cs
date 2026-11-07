using Lockflight.Cli.Gates.WindowsTargets;

namespace Lockflight.Cli.Init;

/// <summary>
/// What <c>init</c> writes for each CI platform, and what it tells the person to do next. Nothing is ever overwritten: a
/// pipeline file the team already has is left as it is, and the Lockflight job goes into a file of its own when the platform
/// can include one (GitLab) or into a copy to merge by hand when it cannot (Bitbucket).
/// </summary>
internal static class CiPipeline
{
    public const string GuideUrl = "https://github.com/lockflight/lockflight/blob/main/docs/guides/ci-other-platforms.md";
    public const string GitHubGuideUrl = "https://github.com/lockflight/lockflight/blob/main/docs/guides/ci-hardening.md";

    /// <summary>The file that holds the Lockflight job on <paramref name="platform"/> when the repository has no pipeline yet.</summary>
    public static string PathFor(CiPlatform platform) => platform switch
    {
        CiPlatform.Azure => AzurePipelinesTemplate.Path,
        CiPlatform.Bitbucket => BitbucketPipelinesTemplate.Path,
        CiPlatform.GitLab => GitLabCiTemplate.Path,
        _ => Templates.WorkflowPath,
    };

    /// <summary>The files to write when missing, the one holding the job first.</summary>
    public static IReadOnlyList<CiFile> Files(CiPlatform platform, string root, IReadOnlyList<string> dotnetChannels, CiTrigger trigger, bool developmentPin) => platform switch
    {
        CiPlatform.Azure => [new(AzurePipelinesTemplate.Path, AzurePipelinesTemplate.Pipeline(dotnetChannels, trigger, developmentPin))],
        CiPlatform.Bitbucket => [new(BitbucketPath(root), BitbucketPipelinesTemplate.Pipeline(dotnetChannels, trigger, developmentPin))],
        CiPlatform.GitLab =>
        [
            new(GitLabCiTemplate.Path, GitLabCiTemplate.Job(dotnetChannels, trigger, developmentPin)),
            new(GitLabCiTemplate.RootPath, GitLabCiTemplate.Root()),
        ],
        _ => [new(Templates.WorkflowPath, JudgeRunner.Apply(Templates.Workflow(dotnetChannels, trigger, developmentPin), windows: WindowsTargeting.WindowsTestProjects(BuildScope.OfRepository(root)).Count > 0))],
    };

    /// <summary>
    /// What the person does next once CI files were written. A GitLab pipeline that does not include the job yet is named on every
    /// run: until it does, the job never runs. A GitHub workflow that runs on pull requests comes with the step a new repository
    /// may need before any workflow runs; GitHub's other settings are in the CI guide.
    /// </summary>
    public static IReadOnlyList<string> NextSteps(CiPlatform platform, string root, IReadOnlyList<InitStep> steps, CiTrigger trigger)
    {
        bool written = steps.Any(step => step.Outcome != FileOutcome.Unchanged);
        bool gitLabWithoutInclude = platform == CiPlatform.GitLab && !GitLabCiTemplate.IsIncludedBy(ReadOrEmpty(root, GitLabCiTemplate.RootPath));
        return written || gitLabWithoutInclude
            ? [.. Instructions(platform, steps, gitLabWithoutInclude, trigger), .. written ? RunnerLines(platform, WindowsTargeting.WindowsTestProjects(BuildScope.OfRepository(root)), root) : []]
            : [];
    }

    /// <summary>
    /// Why the judge runs where it does when a test project needs Windows: on GitHub the building jobs moved to Windows and what
    /// that costs; elsewhere the template's Linux image cannot run those tests, so the job needs a Windows agent.
    /// </summary>
    public static IReadOnlyList<string> RunnerLines(CiPlatform platform, IReadOnlyList<string> windowsTestProjects, string root)
    {
        if (windowsTestProjects.Count == 0)
        {
            return [];
        }

        string projects = string.Join(", ", windowsTestProjects.Select(project => Path.GetRelativePath(root, project).Replace('\\', '/')));
        string needs = $"{projects} {(windowsTestProjects.Count == 1 ? "targets" : "target")} Windows (net*-windows, WPF or Windows Forms), whose tests run only on Windows";
        return platform == CiPlatform.GitHub
            ? [$"CI: {needs}, so the judge and review jobs of {Templates.WorkflowPath} run on {JudgeRunner.WindowsLabel} (the report jobs stay on Linux). The judge builds the fork point, each red commit and head there; a Windows run takes about twice as long as a Linux one and GitHub bills Windows minutes at twice the Linux rate on private repositories, so expect about four times the billed minutes: {GitHubGuideUrl}#desktop-solutions-and-windows-runners"]
            : [$"CI: {needs}; the {PlatformName(platform)} template runs on a Linux image, where the judge stops with NEEDS_WINDOWS_RUNNER. Run the Lockflight job on a Windows agent: {GuideUrl}#platforms-and-runners"];
    }

    private static IReadOnlyList<string> Instructions(CiPlatform platform, IReadOnlyList<InitStep> steps, bool gitLabWithoutInclude, CiTrigger trigger) => platform switch
    {
        CiPlatform.GitHub when trigger == CiTrigger.PullRequest =>
        [
            $"CI: on a new GitHub repository, open the Actions tab and enable workflows if it says \"Workflows aren't being run on this repository\", then push or open a pull request; until {Templates.WorkflowPath} has run, a required gates check waits as \"Expected — Waiting for status to be reported\": {GitHubGuideUrl}#enable-workflows-on-a-new-repository",
        ],
        CiPlatform.Azure =>
        [
            $"CI: in Azure DevOps, create a pipeline from {AzurePipelinesTemplate.Path} and require it with a build validation policy on main; route the file to a required reviewer: {GuideUrl}#azure-pipelines",
        ],
        CiPlatform.Bitbucket when steps.Any(step => step.RelativePath == BitbucketPipelinesTemplate.FragmentPath) =>
        [
            $"CI: {BitbucketPipelinesTemplate.Path} already exists and was left as it is. Bitbucket cannot include a file, so merge the Lockflight steps and their pipelines from {BitbucketPipelinesTemplate.FragmentPath} into it, then make a successful build a merge check: {GuideUrl}#bitbucket-pipelines",
        ],
        CiPlatform.Bitbucket => [$"CI: make a successful build of {BitbucketPipelinesTemplate.Path} a merge check (enforced on Bitbucket Premium only): {GuideUrl}#bitbucket-pipelines"],
        CiPlatform.GitLab when gitLabWithoutInclude =>
        [
            $"CI: {GitLabCiTemplate.RootPath} already exists and was left as it is. Add the Lockflight job to it, then turn on \"Pipelines must succeed\":",
            .. GitLabCiTemplate.Include.Split('\n').Select(line => "  " + line),
            $"  {GuideUrl}#gitlab-cicd",
        ],
        CiPlatform.GitLab => [$"CI: turn on \"Pipelines must succeed\" for merge requests; route {GitLabCiTemplate.Path} and {GitLabCiTemplate.RootPath} to a code owner: {GuideUrl}#gitlab-cicd"],
        _ => [],
    };

    private static string PlatformName(CiPlatform platform) => platform switch
    {
        CiPlatform.Azure => "Azure Pipelines",
        CiPlatform.Bitbucket => "Bitbucket Pipelines",
        CiPlatform.GitLab => "GitLab CI/CD",
        _ => "GitHub",
    };

    /// <summary>
    /// The Bitbucket pipeline file when it is missing or already runs the Lockflight step; otherwise the copy to merge by hand,
    /// so the team's file is never overwritten.
    /// </summary>
    /// <summary>Whether a pipeline file already runs Lockflight: the CI judge, or the gates of an older template.</summary>
    private static bool RunsLockflight(string text) =>
        text.Contains("lockflight gate run", StringComparison.Ordinal) || text.Contains("lockflight ci ", StringComparison.Ordinal);

    private static string BitbucketPath(string root) =>
        File.Exists(Path.Combine(root, BitbucketPipelinesTemplate.Path)) && !RunsLockflight(ReadOrEmpty(root, BitbucketPipelinesTemplate.Path))
            ? BitbucketPipelinesTemplate.FragmentPath
            : BitbucketPipelinesTemplate.Path;

    private static string ReadOrEmpty(string root, string relativePath)
    {
        string path = Path.Combine(root, relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }
}
