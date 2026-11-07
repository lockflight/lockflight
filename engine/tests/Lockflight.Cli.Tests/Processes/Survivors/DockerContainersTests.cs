using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Processes.Survivors;

namespace Lockflight.Cli.Tests.Processes.Survivors;

public sealed class DockerContainersTests
{
    private const string Existing = "aaaa";
    private const string Created = "bbbb";

    [Theory]
    [InlineData((int)JudgePlatform.GitHub, "github-hosted", true)]
    [InlineData((int)JudgePlatform.GitHub, "self-hosted", false)]
    [InlineData((int)JudgePlatform.Bitbucket, null, false)]
    [InlineData((int)JudgePlatform.GitLab, null, false)]
    [InlineData((int)JudgePlatform.Local, "github-hosted", false)]
    public void ContainersAreRemovedOnlyOnAGitHubHostedRunner(int platform, string? runnerEnvironment, bool removes)
    {
        Dictionary<string, string> variables = runnerEnvironment is null ? [] : new() { ["RUNNER_ENVIRONMENT"] = runnerEnvironment };

        Assert.Equal(removes, CiCommand.RemovesContainers((JudgePlatform)platform, EnvironmentReader.Of(variables)));
    }

    [Fact]
    public async Task AContainerStillListedAfterItsRemovalIsNotStopped()
    {
        FakeDocker docker = new([Existing], [Existing, Created]);
        DockerContainers containers = (await DockerContainers.WatchAsync(remove: true, docker.RunAsync, CancellationToken.None))!;

        IReadOnlyList<SweptProcess> found = await containers.SweepAsync(CancellationToken.None);

        Assert.Equal(SurvivorOutcome.NotStopped, Assert.Single(found).Outcome);
        Assert.Null(containers.Note);
    }

    [Fact]
    public async Task ARemovalDockerDoesNotConfirmIsANoteNotAContainerLeftRunning()
    {
        FakeDocker docker = new([Existing], null);
        DockerContainers containers = (await DockerContainers.WatchAsync(remove: true, docker.RunAsync, CancellationToken.None))!;

        IReadOnlyList<SweptProcess> found = await containers.SweepAsync(CancellationToken.None);

        Assert.Empty(found);
        Assert.Contains("was not checked", containers.Note, StringComparison.Ordinal);
        Assert.Contains("rm -f", string.Join(' ', docker.Calls.Select(call => string.Join(' ', call))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARunThatOnlyListsContainersNeverRemovesThem()
    {
        FakeDocker docker = new([Existing], [Existing, Created]);
        DockerContainers containers = (await DockerContainers.WatchAsync(remove: false, docker.RunAsync, CancellationToken.None))!;

        IReadOnlyList<SweptProcess> found = await containers.SweepAsync(CancellationToken.None);

        Assert.Equal(SurvivorOutcome.Observed, Assert.Single(found).Outcome);
        Assert.DoesNotContain(docker.Calls, call => call[0] == "rm");
    }

    /// <summary>
    /// A Docker client whose first id listing answers <c>before</c>, whose full listing shows <see cref="Created"/> running, and whose
    /// later id listings answer <c>after</c> (null: no answer in time).
    /// </summary>
    private sealed class FakeDocker(IReadOnlyList<string> before, IReadOnlyList<string>? after)
    {
        private int idListings;

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeLimit, CancellationToken cancellationToken)
        {
            Calls.Add(arguments);
            ProcessResult result = arguments switch
            {
                ["ps", "-aq", ..] => idListings++ == 0 ? Answer(before) : after is null ? new ProcessResult(-1, string.Empty, string.Empty, 30_000, TimedOut: true) : Answer(after),
                ["ps", "-a", ..] => new ProcessResult(0, $"{Existing}\talpine\trunning\t\"sleep 1\"\n{Created}\talpine:3.20\trunning\t\"sleep 600\"\n", string.Empty, 1, TimedOut: false),
                _ => new ProcessResult(0, string.Empty, string.Empty, 1, TimedOut: false),
            };
            return Task.FromResult(result);
        }

        private static ProcessResult Answer(IReadOnlyList<string> ids) => new(0, string.Join('\n', ids), string.Empty, 1, TimedOut: false);
    }
}
