namespace Lockflight.Cli.Init;

/// <summary>
/// What <c>init</c> writes for each CI platform, and what it tells the person to do next. Nothing is ever overwritten: a
/// pipeline file the team already has is left as it is, and the Lockflight job goes into a file of its own when the platform
/// can include one (GitLab) or into a copy to merge by hand when it cannot (Bitbucket).
/// </summary>
internal static class CiPipeline
{
    public const string GuideUrl = "https://github.com/lockflight/lockflight/blob/main/docs/guides/ci-other-platforms.md";

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
        _ => [new(Templates.WorkflowPath, Templates.Workflow(dotnetChannels, trigger, developmentPin))],
    };

    /// <summary>
    /// What the person does next once CI files were written; nothing for GitHub, whose docs cover it. A GitLab pipeline that
    /// does not include the job yet is named on every run: until it does, the job never runs.
    /// </summary>
    public static IReadOnlyList<string> NextSteps(CiPlatform platform, string root, IReadOnlyList<InitStep> steps)
    {
        bool written = steps.Any(step => step.Outcome != FileOutcome.Unchanged);
        bool gitLabWithoutInclude = platform == CiPlatform.GitLab && !GitLabCiTemplate.IsIncludedBy(ReadOrEmpty(root, GitLabCiTemplate.RootPath));
        return written || gitLabWithoutInclude ? Instructions(platform, steps, gitLabWithoutInclude) : [];
    }

    private static IReadOnlyList<string> Instructions(CiPlatform platform, IReadOnlyList<InitStep> steps, bool gitLabWithoutInclude) => platform switch
    {
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
