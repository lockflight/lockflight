using Lockflight.Cli.Init;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Tests.Init;

public sealed class CiPipelineTests
{
    private static readonly string[] OneChannel = [DotnetChannels.EngineChannel];

    public static TheoryData<string> Platforms() => ["github", "azure", "bitbucket", "gitlab"];

    public static TheoryData<string> OtherPlatforms() => ["azure", "bitbucket", "gitlab"];

    [Theory]
    [MemberData(nameof(Platforms))]
    public void EveryPlatformRestoresTheSamePolicyFromTheBaseAsTheGitHubWorkflow(string platform)
    {
        string pipeline = JobFile(platform, OneChannel, CiTrigger.PullRequest);

        Assert.Contains($"for file in {CiShellScripts.PolicyFiles}; do", pipeline, StringComparison.Ordinal);
        Assert.Contains("git checkout \"$BASE\" -- \"$file\"; else rm -f \"$file\"; fi", pipeline, StringComparison.Ordinal);
        Assert.Contains("rm -rf .lockflight/baselines", pipeline, StringComparison.Ordinal);
        Assert.Contains("git checkout \"$BASE\" -- .lockflight/baselines", pipeline, StringComparison.Ordinal);
        Assert.Contains($"style={CiShellScripts.StylePattern}", pipeline, StringComparison.Ordinal);
        Assert.Contains("{ git ls-tree -r --name-only \"$BASE\"; git ls-files; }", pipeline, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void EveryPlatformPinsGitleaksAndTheEngineAndRunsThePrGates(string platform)
    {
        string pipeline = JobFile(platform, OneChannel, CiTrigger.PullRequest);

        Assert.Contains(CiShellScripts.GitleaksVersion, pipeline, StringComparison.Ordinal);
        Assert.Contains(CiShellScripts.GitleaksSha256, pipeline, StringComparison.Ordinal);
        Assert.Contains("| sha256sum -c -", pipeline, StringComparison.Ordinal);
        Assert.Contains("dotnet tool install Lockflight.Cli --tool-path", pipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet tool restore", pipeline, StringComparison.Ordinal);
        Assert.Contains("--base \"$BASE\"", pipeline, StringComparison.Ordinal);
        if (platform == "bitbucket")
        {
            Assert.Contains("lockflight ci --platform bitbucket", pipeline, StringComparison.Ordinal);
            return;
        }

        Assert.Contains(CiShellScripts.GateCommand, pipeline, StringComparison.Ordinal);
        Assert.Contains("--result artifacts/lockflight/result.json", pipeline, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBitbucketPipelineJudgesThenReportsEachStatusContextInParallelWithoutASecret()
    {
        string pipeline = JobFile("bitbucket", OneChannel, CiTrigger.PullRequest);

        Assert.Contains("--head \"$HEAD_COMMIT\"", pipeline, StringComparison.Ordinal);
        Assert.Contains("${BITBUCKET_BRANCH:+--branch \"$BITBUCKET_BRANCH\"}", pipeline, StringComparison.Ordinal);
        Assert.Contains("lockflight ci report --evidence artifacts/lockflight-evidence --head \"$HEAD_COMMIT\" --context gates", pipeline, StringComparison.Ordinal);
        Assert.Contains("--context lockflight/judge", pipeline, StringComparison.Ordinal);
        Assert.Contains("- parallel:", pipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("ANTHROPIC_API_KEY", pipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("ci review", pipeline, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("azure", "SYSTEM_PULLREQUEST_TARGETBRANCH")]
    [InlineData("azure", "git merge-base HEAD \"origin/$target\"")]
    [InlineData("bitbucket", "BITBUCKET_PR_DESTINATION_BRANCH")]
    [InlineData("bitbucket", "BASE=$(git rev-parse \"origin/$PULL_REQUEST\")")]
    [InlineData("gitlab", "BASE=\"$CI_MERGE_REQUEST_DIFF_BASE_SHA\"")]
    public void EachPlatformFindsTheBaseFromItsOwnPullRequestVariables(string platform, string expected) =>
        Assert.Contains(expected, JobFile(platform, OneChannel, CiTrigger.PullRequest), StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(OtherPlatforms))]
    public void EveryPipelineIsValidYamlWhoseScriptsRunInOrder(string platform)
    {
        string scripts = string.Join('\n', Scripts(Parse(JobFile(platform, ["8.0.x", "10.0.x"], CiTrigger.PullRequest))));

        int restore = scripts.IndexOf("for file in", StringComparison.Ordinal);
        int gitleaks = scripts.IndexOf("sha256sum -c -", StringComparison.Ordinal);
        int engine = scripts.IndexOf("dotnet tool install", StringComparison.Ordinal);
        int gates = scripts.IndexOf(platform == "bitbucket" ? "lockflight ci --platform" : CiShellScripts.GateCommand, StringComparison.Ordinal);
        Assert.True(restore >= 0 && restore < gitleaks && gitleaks < engine && engine < gates, scripts);
    }

    [Theory]
    [InlineData("bitbucket")]
    [InlineData("gitlab")]
    public void AContainerJobInstallsTheTestProjectsOlderChannelNextToTheImagesSdk(string platform)
    {
        string pipeline = JobFile(platform, ["8.0.x", "10.0.x"], CiTrigger.PullRequest);

        Assert.Contains("--channel 8.0 --install-dir /usr/share/dotnet", pipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("--channel 10.0", pipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet-install.sh", JobFile(platform, OneChannel, CiTrigger.PullRequest), StringComparison.Ordinal);
    }

    [Fact]
    public void TheAzurePipelineInstallsEveryChannelWithUseDotNet()
    {
        string pipeline = AzurePipelinesTemplate.Pipeline(["8.0.x", "10.0.x"], CiTrigger.PullRequest, developmentPin: false);

        Assert.Contains("version: 8.0.x", pipeline, StringComparison.Ordinal);
        Assert.Contains("version: 10.0.x", pipeline, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("azure", "pr: none")]
    [InlineData("bitbucket", "custom:")]
    [InlineData("gitlab", "$CI_PIPELINE_SOURCE == \"web\"")]
    public void AManualPipelineRunsOnlyWhenStartedByHandAndSaysWhy(string platform, string manualTrigger)
    {
        string manual = JobFile(platform, OneChannel, CiTrigger.Manual);
        string preview = JobFile(platform, OneChannel, CiTrigger.Manual, developmentPin: true);

        Assert.Contains(manualTrigger, manual, StringComparison.Ordinal);
        Assert.Contains("lockflight init --ci manual", manual, StringComparison.Ordinal);
        Assert.Contains("CI during the preview", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("pull-requests:", manual, StringComparison.Ordinal);
        Assert.DoesNotContain("merge_request_event\"\n", manual, StringComparison.Ordinal);
        Parse(manual);
        Parse(preview);
    }

    [Theory]
    [MemberData(nameof(OtherPlatforms))]
    public void EveryNewPipelineSaysItIsNotYetVerifiedOnTheLivePlatform(string platform) =>
        Assert.Contains("Not yet verified", JobFile(platform, OneChannel, CiTrigger.PullRequest), StringComparison.Ordinal);

    [Fact]
    public void TheGitLabRootPipelineOnlyIncludesTheJob()
    {
        YamlMappingNode root = Parse(GitLabCiTemplate.Root());

        YamlSequenceNode include = Assert.IsType<YamlSequenceNode>(Assert.Single(root.Children).Value);
        Assert.Equal("/" + GitLabCiTemplate.Path, ((YamlScalarNode)((YamlMappingNode)include.Children[0])["local"]).Value);
        Assert.True(GitLabCiTemplate.IsIncludedBy(GitLabCiTemplate.Root()));
    }

    private static string JobFile(string platformName, IReadOnlyList<string> channels, CiTrigger trigger, bool developmentPin = false)
    {
        Assert.True(CiPlatformNames.TryParse(platformName, out CiPlatform platform));
        return CiPipeline.Files(platform, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), channels, trigger, developmentPin)[0].Content;
    }

    private static YamlMappingNode Parse(string yaml)
    {
        YamlStream stream = new();
        stream.Load(new StringReader(yaml));
        return Assert.IsType<YamlMappingNode>(stream.Documents[0].RootNode);
    }

    /// <summary>Every multi-line scalar in document order: the shell scripts of the steps.</summary>
    private static IEnumerable<string> Scripts(YamlNode node) => node switch
    {
        YamlScalarNode { Value: string value } when value.Contains('\n', StringComparison.Ordinal) => [value],
        YamlMappingNode mapping => mapping.Children.SelectMany(child => Scripts(child.Value)),
        YamlSequenceNode sequence => sequence.Children.SelectMany(Scripts),
        _ => [],
    };
}
