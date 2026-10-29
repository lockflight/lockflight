using Lockflight.Cli.Config;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class TemplatesTests
{
    [Fact]
    public void TheManifestCommentNamesTheProfileThatWroteIt()
    {
        Assert.Contains("lite profile", Templates.Manifest(InitProfile.Lite), StringComparison.Ordinal);
        Assert.Contains("standard profile", Templates.Manifest(InitProfile.Standard), StringComparison.Ordinal);
        Assert.DoesNotContain("lite profile", Templates.Manifest(InitProfile.Standard), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lite")]
    [InlineData("standard")]
    public void EachProfilesManifestLoads(string profileName)
    {
        Assert.True(InitProfileNames.TryParse(profileName, out InitProfile profile));

        ManifestLoadResult loaded = new ManifestParser("/project", "/project/lockflight.yaml").Parse(Templates.Manifest(profile));

        Assert.NotNull(loaded.Manifest);
        Assert.False(loaded.Manifest.StopGate);
    }

    [Fact]
    public void TheWorkflowRunsTheCiJudgeActionPinnedByCommitInFourJobs()
    {
        string workflow = Templates.Workflow(["8.0.x", "10.0.x"]);

        Assert.Matches(@"uses: lockflight/lockflight/action@[0-9a-f]{40} # ", workflow);
        Assert.Contains("  judge:\n", workflow, StringComparison.Ordinal);
        Assert.Contains("  review:\n", workflow, StringComparison.Ordinal);
        Assert.Contains("  gates:\n", workflow, StringComparison.Ordinal);
        Assert.Contains("    name: lockflight/judge\n", workflow, StringComparison.Ordinal);
        Assert.Contains("mode: judge", workflow, StringComparison.Ordinal);
        Assert.Contains("context: gates", workflow, StringComparison.Ordinal);
        Assert.Contains("context: lockflight/judge", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request_target", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPullRequestValueIsInterpolatedIntoAScriptAndOnlyTheReportPostsComments()
    {
        string[] lines = Templates.Workflow(["10.0.x"]).Split('\n');
        List<string> interpolated = [];
        int? scriptIndent = null;
        foreach (string line in lines)
        {
            int indent = line.Length - line.TrimStart().Length;
            if (scriptIndent is int block && line.Trim().Length > 0 && indent <= block)
            {
                scriptIndent = null;
            }

            if ((scriptIndent is not null || line.TrimStart().StartsWith("run:", StringComparison.Ordinal)) && line.Contains("${{", StringComparison.Ordinal))
            {
                interpolated.Add(line.Trim());
            }

            if (line.TrimStart().StartsWith("run: |", StringComparison.Ordinal))
            {
                scriptIndent = indent;
            }
        }

        Assert.Empty(interpolated);
        Assert.Equal(1, lines.Count(line => line.Trim() == "pull-requests: write"));
        Assert.Equal(1, lines.Count(line => line.Contains("secrets.ANTHROPIC_API_KEY", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheReviewRunsOnlyForSameRepositoryPullRequestsWithTheVariableSetAndNotAfterAJudgeFail()
    {
        string workflow = Templates.Workflow(["10.0.x"]);

        Assert.Contains("vars.LOCKFLIGHT_REVIEW == 'true'", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event.pull_request.head.repo.full_name == github.repository", workflow, StringComparison.Ordinal);
        Assert.Contains("needs.judge.outputs.verdict != 'FAIL'", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void AManualWorkflowForADevelopmentPinSaysWhyItRunsOnlyByHand()
    {
        string workflow = Templates.Workflow(["10.0.x"], CiTrigger.Manual, developmentPin: true);

        Assert.Contains("on:\n  workflow_dispatch:\n", workflow, StringComparison.Ordinal);
        Assert.Contains("CI during the preview", workflow, StringComparison.Ordinal);
        Assert.Contains("development build", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void AManualWorkflowForAPublishedPinNamesTheOption()
    {
        string workflow = Templates.Workflow(["10.0.x"], CiTrigger.Manual, developmentPin: false);

        Assert.Contains("lockflight init --ci manual", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("CI during the preview", workflow, StringComparison.Ordinal);
    }
}
