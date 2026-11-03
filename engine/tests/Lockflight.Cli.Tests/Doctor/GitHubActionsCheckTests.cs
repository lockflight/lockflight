using System.ComponentModel;
using Lockflight.Cli.Doctor;
using Lockflight.Cli.Init;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Doctor;

public sealed class GitHubActionsCheckTests : IDisposable
{
    private const string Remote = "https://github.com/acme/shop.git";
    private const string NoRuns = """{ "total_count": 0, "workflow_runs": [] }""";
    private const string OneRun = """{ "total_count": 1, "workflow_runs": [ { "event": "push" } ] }""";
    private const string NotAFork = """{ "full_name": "acme/shop", "fork": false }""";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-actions-");
    private readonly List<string> calls = [];

    [Fact]
    public async Task AWorkflowThatGitHubNeverRanOnPushOrPullRequestIsAWarningThatSaysHowToEnableIt()
    {
        WriteWorkflow(CiTrigger.PullRequest);

        DoctorCheck? check = await GitHubActionsCheck.CheckAsync(root.FullName, Fake(Remote, Gh()), TestContext.Current.CancellationToken);

        Assert.NotNull(check);
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("acme/shop", check.Detail, StringComparison.Ordinal);
        Assert.Contains("not be enabled", check.Detail, StringComparison.Ordinal);
        Assert.Contains("https://github.com/acme/shop/actions", check.Fix, StringComparison.Ordinal);
        Assert.Contains("enable", check.Fix, StringComparison.Ordinal);
        Assert.Contains(calls, call => call.StartsWith("gh api", StringComparison.Ordinal) && call.EndsWith("repos/acme/shop/actions/workflows/lockflight-gates.yml/runs?event=push&per_page=1", StringComparison.Ordinal));
        Assert.Contains(calls, call => call.EndsWith("runs?event=pull_request&per_page=1", StringComparison.Ordinal));
        Assert.All(calls.Where(call => call.StartsWith("gh ", StringComparison.Ordinal)), call => Assert.Contains("--hostname github.com", call, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AForkIsSkippedBecauseItsPullRequestsRunUpstream()
    {
        WriteWorkflow(CiTrigger.PullRequest);

        DoctorCheck? check = await GitHubActionsCheck.CheckAsync(
            root.FullName, Fake(Remote, Gh(repository: """{ "full_name": "me/shop", "fork": true }""")), TestContext.Current.CancellationToken);

        Assert.Null(check);
        Assert.DoesNotContain(calls, call => call.Contains("/runs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("push")]
    [InlineData("pull_request")]
    public async Task ARunOnPushOrPullRequestPasses(string trigger)
    {
        WriteWorkflow(CiTrigger.PullRequest);

        DoctorCheck? check = await GitHubActionsCheck.CheckAsync(
            root.FullName, Fake(Remote, Gh(push: trigger == "push" ? OneRun : NoRuns, pullRequest: trigger == "pull_request" ? OneRun : NoRuns)), TestContext.Current.CancellationToken);

        Assert.Equal(CheckStatus.Pass, check?.Status);
    }

    [Fact]
    public async Task AManualWorkflowIsNotCheckedAndNothingIsStarted()
    {
        WriteWorkflow(CiTrigger.Manual);

        Assert.Null(await GitHubActionsCheck.CheckAsync(root.FullName, Fake(Remote, Gh()), TestContext.Current.CancellationToken));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task WithoutTheGeneratedWorkflowNothingIsStarted()
    {
        Assert.Null(await GitHubActionsCheck.CheckAsync(root.FullName, Fake(Remote, Gh()), TestContext.Current.CancellationToken));
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData("https://gitlab.com/acme/shop.git")]
    [InlineData("git@example.com:acme/shop.git")]
    [InlineData("")]
    public async Task ARemoteThatIsNotOnGitHubIsSkippedWithoutCallingGh(string remote)
    {
        WriteWorkflow(CiTrigger.PullRequest);

        Assert.Null(await GitHubActionsCheck.CheckAsync(root.FullName, Fake(remote, Gh()), TestContext.Current.CancellationToken));
        Assert.DoesNotContain(calls, call => call.StartsWith("gh ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoOriginRemoteIsSkipped()
    {
        WriteWorkflow(CiTrigger.PullRequest);

        Assert.Null(await GitHubActionsCheck.CheckAsync(root.FullName, Fake(remote: null, Gh()), TestContext.Current.CancellationToken));
        Assert.DoesNotContain(calls, call => call.StartsWith("gh ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMissingGhIsSkipped()
    {
        WriteWorkflow(CiTrigger.PullRequest);
        RunTimedProcess processes = (fileName, arguments, workingDirectory, timeLimit, cancellationToken) => fileName == "gh"
            ? throw new ExecutableNotFoundException(fileName, new Win32Exception())
            : Task.FromResult(new ProcessResult(0, Remote + "\n", string.Empty, 1, TimedOut: false));

        Assert.Null(await GitHubActionsCheck.CheckAsync(root.FullName, processes, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUnauthenticatedOrOfflineGhIsSkipped()
    {
        WriteWorkflow(CiTrigger.PullRequest);

        Assert.Null(await GitHubActionsCheck.CheckAsync(root.FullName, Fake(Remote, _ => Ran(4, string.Empty)), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AGhThatTimesOutIsSkippedAndRunsUnderAShortTimeLimit()
    {
        WriteWorkflow(CiTrigger.PullRequest);
        List<TimeSpan> limits = [];
        RunTimedProcess processes = (fileName, arguments, workingDirectory, timeLimit, cancellationToken) =>
        {
            limits.Add(timeLimit);
            return Task.FromResult(fileName == "gh"
                ? new ProcessResult(ProcessRunner.TimedOutExitCode, string.Empty, string.Empty, 1, TimedOut: true)
                : new ProcessResult(0, Remote + "\n", string.Empty, 1, TimedOut: false));
        };

        Assert.Null(await GitHubActionsCheck.CheckAsync(root.FullName, processes, TestContext.Current.CancellationToken));
        Assert.All(limits, limit => Assert.InRange(limit, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public async Task OutputThatIsNotTheRunsListIsSkipped()
    {
        WriteWorkflow(CiTrigger.PullRequest);

        Assert.Null(await GitHubActionsCheck.CheckAsync(root.FullName, Fake(Remote, _ => Ran(0, "<html>")), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("on: [push, pull_request]\njobs: {}\n")]
    [InlineData("on: pull_request\njobs: {}\n")]
    [InlineData("on:\n  push:\n    branches: [main]\n")]
    public async Task AHandWrittenWorkflowWithPushOrPullRequestTriggersIsChecked(string workflow)
    {
        Write(workflow);

        Assert.Equal(CheckStatus.Warn, (await GitHubActionsCheck.CheckAsync(root.FullName, Fake(Remote, Gh()), TestContext.Current.CancellationToken))?.Status);
    }

    [Theory]
    [InlineData("on:\n  workflow_dispatch:\njobs:\n  image:\n    steps:\n      - uses: docker/build-push-action@v6\n        with:\n          push: true\n")]
    [InlineData("on: pull_request_target\njobs: {}\n")]
    [InlineData("on: [push\n")]
    public async Task AWorkflowWithoutThoseTriggersOrThatIsNotYamlIsNotChecked(string workflow)
    {
        Write(workflow);

        Assert.Null(await GitHubActionsCheck.CheckAsync(root.FullName, Fake(Remote, Gh()), TestContext.Current.CancellationToken));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task AWorkflowPathThatCannotBeReadIsSkipped()
    {
        Directory.CreateDirectory(Path.Combine(root.FullName, Templates.WorkflowPath));

        Assert.Null(await GitHubActionsCheck.CheckAsync(root.FullName, Fake(Remote, Gh()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AGitThatTimesOutIsSkipped()
    {
        WriteWorkflow(CiTrigger.PullRequest);
        RunTimedProcess processes = (fileName, arguments, workingDirectory, timeLimit, cancellationToken) =>
            Task.FromResult(new ProcessResult(ProcessRunner.TimedOutExitCode, Remote, string.Empty, 1, TimedOut: true));

        Assert.Null(await GitHubActionsCheck.CheckAsync(root.FullName, processes, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AGhThatCannotStartIsSkipped()
    {
        WriteWorkflow(CiTrigger.PullRequest);
        RunTimedProcess processes = (fileName, arguments, workingDirectory, timeLimit, cancellationToken) => fileName == "gh"
            ? throw new InvalidOperationException("could not start gh")
            : Task.FromResult(Ran(0, Remote));

        Assert.Null(await GitHubActionsCheck.CheckAsync(root.FullName, processes, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("https://github.com/acme/shop.git", "acme/shop")]
    [InlineData("https://github.com/acme/shop", "acme/shop")]
    [InlineData("https://github.com/acme/shop/", "acme/shop")]
    [InlineData("https://git@github.com/acme/shop.git", "acme/shop")]
    [InlineData("git@github.com:acme/shop.git", "acme/shop")]
    [InlineData("ssh://git@github.com/acme/shop.git", "acme/shop")]
    [InlineData("https://GitHub.com/acme/shop.git", "acme/shop")]
    [InlineData("https://github.com.evil.example/acme/shop.git", null)]
    [InlineData("https://gitlab.com/acme/shop.git", null)]
    [InlineData("https://github.com/acme", null)]
    [InlineData("/srv/git/shop.git", null)]
    [InlineData("git@github.com:../shop.git", null)]
    [InlineData("https://github.com/acme/shop?x=1", null)]
    [InlineData("git@github.com:acme/.git", null)]
    public void TheRepositoryIsReadFromAGitHubRemote(string remote, string? expected) =>
        Assert.Equal(expected, GitHubActionsCheck.RepositoryOf(remote));

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static ProcessResult Ran(int exitCode, string output) => new(exitCode, output, string.Empty, 1, TimedOut: false);

    // gh's answers by endpoint: the repository, its push runs and its pull request runs.
    private static Func<string, ProcessResult> Gh(string repository = NotAFork, string push = NoRuns, string pullRequest = NoRuns) => endpoint =>
        endpoint.Contains("event=push", StringComparison.Ordinal) ? Ran(0, push)
        : endpoint.Contains("event=pull_request", StringComparison.Ordinal) ? Ran(0, pullRequest)
        : Ran(0, repository);

    private RunTimedProcess Fake(string? remote, Func<string, ProcessResult> gh) => (fileName, arguments, workingDirectory, timeLimit, cancellationToken) =>
    {
        calls.Add($"{fileName} {string.Join(' ', arguments)}");
        return Task.FromResult(fileName == "gh"
            ? gh(arguments[^1])
            : remote is null ? Ran(2, string.Empty) : Ran(0, remote + "\n"));
    };

    private void WriteWorkflow(CiTrigger trigger) => Write(Templates.Workflow(["10.0.x"], trigger, developmentPin: false));

    private void Write(string workflow)
    {
        Directory.CreateDirectory(Path.Combine(root.FullName, ".github", "workflows"));
        File.WriteAllText(Path.Combine(root.FullName, Templates.WorkflowPath), workflow);
    }
}
