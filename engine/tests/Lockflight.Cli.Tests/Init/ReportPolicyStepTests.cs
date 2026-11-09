using Lockflight.Cli.Init;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Init;

/// <summary>
/// The report jobs' policy step of the GitHub workflow, run as the runner runs it (bash) in a scratch clone whose target branch
/// is <c>origin/main</c>: base, then two commits after it, and a pull request commit off the base. The judge job's
/// <c>policy</c> output is what the pull request's code could set; the step takes it only when it is on the target branch between
/// the event's base and the tip, on its first-parent chain.
/// </summary>
public sealed class ReportPolicyStepTests : IDisposable
{
    private const string Zero = "0000000000000000000000000000000000000000";

    private readonly DirectoryInfo repository = Directory.CreateTempSubdirectory("lockflight-report-policy-");

    [Fact]
    public async Task AJudgePolicyOnTheTargetBranchAfterTheEventsBaseIsUsed()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The report jobs run on Linux.");
        Commits commits = await CommitsAsync();

        StepRun run = await RunAsync(claimed: commits.Middle, target: "main", eventBase: commits.Base);

        Assert.True(run.Process.ExitCode == 0, run.Process.Error + run.Process.Output);
        Assert.Equal(commits.Middle, run.Sha);
        Assert.Equal(commits.Middle, await HeadAsync());
        Assert.DoesNotContain("::error::", run.Process.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APolicyCommitOfThePullRequestIsReplacedByTheEventsBase()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The report jobs run on Linux.");
        Commits commits = await CommitsAsync();

        StepRun run = await RunAsync(claimed: commits.PullRequest, target: "main", eventBase: commits.Base);

        Assert.True(run.Process.ExitCode == 0, run.Process.Error + run.Process.Output);
        Assert.Equal(commits.Base, run.Sha);
        Assert.Equal(commits.Base, await HeadAsync());
        Assert.Contains("::error::the judge job names the policy commit", run.Process.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APolicyCommitOlderThanTheEventsBaseIsReplacedByIt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The report jobs run on Linux.");
        Commits commits = await CommitsAsync();

        StepRun run = await RunAsync(claimed: commits.Base, target: "main", eventBase: commits.Middle);

        Assert.Equal(commits.Middle, run.Sha);
        Assert.Contains("::error::", run.Process.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACommitOfABranchMergedIntoTheTargetIsReplacedByTheEventsBase()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The report jobs run on Linux.");
        await GitAsync("init", "-q", "-b", "main");
        string baseCommit = await CommitAsync("base");
        await GitAsync("checkout", "-q", "-b", "side");
        string side = await CommitAsync("loosen the policy");
        await CommitAsync("restore the policy");
        await GitAsync("checkout", "-q", "main");
        await GitAsync("-c", "user.email=t@t", "-c", "user.name=t", "merge", "-q", "--no-ff", "-m", "merge side", "side");
        string merge = await HeadAsync();
        await GitAsync("update-ref", "refs/remotes/origin/main", merge);
        await GitAsync("checkout", "-q", "--detach", baseCommit);

        StepRun sideRun = await RunAsync(claimed: side, target: "main", eventBase: baseCommit);
        StepRun mergeRun = await RunAsync(claimed: merge, target: "main", eventBase: baseCommit);

        Assert.Equal(baseCommit, sideRun.Sha);
        Assert.Contains("::error::the judge job names the policy commit", sideRun.Process.Output, StringComparison.Ordinal);
        Assert.Equal(merge, mergeRun.Sha);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--output=/tmp/x")]
    [InlineData("main")]
    [InlineData("ffffffffffffffffffffffffffffffffffffffff")]
    public async Task AMissingOrMalformedPolicyIsReplacedByTheEventsBase(string claimed)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The report jobs run on Linux.");
        Commits commits = await CommitsAsync();

        StepRun run = await RunAsync(claimed, target: "main", eventBase: commits.Base);

        Assert.True(run.Process.ExitCode == 0, run.Process.Error + run.Process.Output);
        Assert.Equal(commits.Base, run.Sha);
    }

    [Fact]
    public async Task APushReadsThePolicyOfThePreviousCommitWhateverTheJudgeSays()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The report jobs run on Linux.");
        Commits commits = await CommitsAsync();

        StepRun run = await RunAsync(claimed: commits.PullRequest, target: string.Empty, eventBase: string.Empty, before: commits.Middle, sha: commits.Tip);

        Assert.Equal(commits.Middle, run.Sha);
    }

    [Fact]
    public async Task APushWithoutAPreviousCommitReadsThePolicyOfThePushedCommit()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The report jobs run on Linux.");
        Commits commits = await CommitsAsync();

        StepRun run = await RunAsync(claimed: commits.PullRequest, target: string.Empty, eventBase: string.Empty, before: Zero, sha: commits.Tip);

        Assert.Equal(commits.Tip, run.Sha);
    }

    [Fact]
    public void TheReportJobsTakeTheirBaseFromThePolicyStepAndVerifyTheArtifacts()
    {
        string workflow = Templates.Workflow(["10.0.x"], CiTrigger.PullRequest, developmentPin: false);
        string[] lines = workflow.Split('\n');

        Assert.DoesNotContain(lines, line => line.Contains("needs.judge.outputs.policy", StringComparison.Ordinal) && !line.Contains("LF_CLAIMED:", StringComparison.Ordinal));
        Assert.Equal(2, lines.Count(line => line.Trim() == "base: ${{ steps.policy.outputs.sha }}"));
        Assert.Equal(2, lines.Count(line => line.Trim() == "verify-artifacts: \"true\""));
        Assert.Equal(2, lines.Count(line => line.Trim() == "evidence-sha256: ${{ needs.judge.outputs.evidence-sha256 }}"));
        Assert.Equal(2, lines.Count(line => line.Trim() == "review-artifact-id: ${{ needs.review.outputs.review-artifact-id }}"));
        Assert.Contains("      evidence-sha256: ${{ steps.judge.outputs.evidence-sha256 }}", lines);
        Assert.Contains("      review-sha256: ${{ steps.review.outputs.review-sha256 }}", lines);
        Assert.Contains("          git -c advice.detachedHead=false checkout --quiet --detach \"$sha\"", lines);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(repository);

    private async Task<Commits> CommitsAsync()
    {
        await GitAsync("init", "-q", "-b", "main");
        string baseCommit = await CommitAsync("base");
        string middle = await CommitAsync("middle");
        string tip = await CommitAsync("tip");
        await GitAsync("update-ref", "refs/remotes/origin/main", tip);
        await GitAsync("checkout", "-q", "-b", "feature", baseCommit);
        string pullRequest = await CommitAsync("pull request");
        await GitAsync("checkout", "-q", "--detach", baseCommit);
        return new Commits(baseCommit, middle, tip, pullRequest);
    }

    private async Task<string> CommitAsync(string message)
    {
        await GitAsync("-c", "user.email=t@t", "-c", "user.name=t", "commit", "-q", "--allow-empty", "-m", message);
        return await HeadAsync();
    }

    private async Task<string> HeadAsync() => (await GitAsync("rev-parse", "HEAD")).Output.Trim();

    private async Task<ProcessResult> GitAsync(params string[] arguments) =>
        await ProcessRunner.RunAsync("git", arguments, repository.FullName, TestContext.Current.CancellationToken);

    private async Task<StepRun> RunAsync(string claimed, string target, string eventBase, string before = "", string sha = "")
    {
        string step = Path.Combine(repository.FullName, ".git", "policy-step.sh");
        string output = Path.Combine(repository.FullName, ".git", "github-output");
        File.WriteAllText(step, Templates.ReportPolicyScript + "\n");
        File.WriteAllText(output, string.Empty);
        string[] environment =
        [
            $"LF_CLAIMED={claimed}", $"LF_TARGET={target}", $"LF_EVENT_BASE={eventBase}", $"LF_BEFORE={before}", $"LF_SHA={sha}",
            $"GITHUB_OUTPUT={output}",
        ];
        ProcessResult process = await ProcessRunner.RunAsync("env", [.. environment, "bash", "-eo", "pipefail", step], repository.FullName, TestContext.Current.CancellationToken);
        string written = File.ReadAllText(output);
        return new StepRun(process, written.StartsWith("sha=", StringComparison.Ordinal) ? written["sha=".Length..].Trim() : string.Empty);
    }

    private sealed record Commits(string Base, string Middle, string Tip, string PullRequest);

    private sealed record StepRun(ProcessResult Process, string Sha);
}
