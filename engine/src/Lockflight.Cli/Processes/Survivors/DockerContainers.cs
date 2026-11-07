namespace Lockflight.Cli.Processes.Survivors;

/// <summary>
/// The Docker containers created while the CI judge runs. The runner's user can start a container without <c>sudo</c>, and a
/// container is not a descendant of the engine (the Docker daemon starts it), so the process sweep does not see it. The judge
/// lists the containers when it starts and, at the end of each part of its run, finds the ones created since, removes them
/// (<c>docker rm -f</c>) and records them; one that could not be removed is not stopped. Only a run on a GitHub-hosted runner removes
/// them: a local run, or one on any other runner (whose Docker daemon other jobs may share), lists them and leaves them. When
/// <c>docker</c> does not answer a later listing, what it would have shown is not checked, and <see cref="Note"/> says so. A
/// guardrail: a container that is created and gone between two checks is not seen.
/// </summary>
internal sealed class DockerContainers(DockerRun docker, IReadOnlySet<string> before, bool remove)
{
    private const char Separator = '\t';
    private const int ListedFields = 4;
    private static readonly TimeSpan FirstAnswerTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ListTime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RemoveTime = TimeSpan.FromSeconds(60);

    private readonly HashSet<string> seen = [.. before];

    /// <summary>
    /// Why some of the containers were not checked (<c>docker</c> did not answer a listing within 30 seconds); null when every
    /// check was answered. The first reason is kept.
    /// </summary>
    public string? Note { get; private set; }

    /// <summary>
    /// Lists the containers that exist now; null when there is no <c>docker</c> on PATH or its daemon does not answer within 2
    /// seconds, so there is nothing to check.
    /// </summary>
    public static Task<DockerContainers?> WatchAsync(bool remove, CancellationToken cancellationToken) =>
        Executables.Find("docker", explicitPath: null) is string docker
            ? WatchAsync(remove, (arguments, timeLimit, token) => RunAsync(docker, arguments, timeLimit, token), cancellationToken)
            : Task.FromResult<DockerContainers?>(null);

    /// <summary>As <see cref="WatchAsync(bool, CancellationToken)"/>, with <paramref name="docker"/> running the client.</summary>
    public static async Task<DockerContainers?> WatchAsync(bool remove, DockerRun docker, CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? ids = await IdsAsync(docker, FirstAnswerTime, cancellationToken);
        return ids is null ? null : new DockerContainers(docker, new HashSet<string>(ids, StringComparer.Ordinal), remove);
    }

    /// <summary>The containers created since the judge started and not yet reported, removed unless this run only lists them.</summary>
    public async Task<IReadOnlyList<SweptProcess>> SweepAsync(CancellationToken cancellationToken)
    {
        ProcessResult listing = await docker(["ps", "-a", "--no-trunc", "--format", "{{.ID}}\t{{.Image}}\t{{.State}}\t{{.Command}}"], ListTime, cancellationToken);
        if (listing.ExitCode != 0 || listing.TimedOut)
        {
            Note ??= "Docker containers created by the pull request's code were not checked at one point: 'docker ps' failed or did not "
                + $"answer within {ListTime.TotalSeconds:0} seconds";
            return [];
        }

        Listed[] fresh = [.. listing.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(Separator, ListedFields))
            .Where(fields => fields.Length == ListedFields && seen.Add(fields[0]))
            .Select(fields => new Listed(fields[0], fields[1], fields[2], fields[3].Trim('"')))];
        if (fresh.Length == 0)
        {
            return [];
        }

        if (!remove)
        {
            return [.. fresh.Select(container => Swept(container, SurvivorOutcome.Observed))];
        }

        await docker(["rm", "-f", .. fresh.Select(container => container.Id)], RemoveTime, cancellationToken);
        if (await IdsAsync(docker, ListTime, cancellationToken) is not IReadOnlyList<string> left)
        {
            // Not answering is not proof that a container survived: the removal is unconfirmed, not failed.
            Note ??= $"{fresh.Length} Docker {(fresh.Length == 1 ? "container" : "containers")} the pull request's code created "
                + $"{(fresh.Length == 1 ? "was" : "were")} removed with 'docker rm -f', but 'docker ps' did not answer within "
                + $"{ListTime.TotalSeconds:0} seconds afterwards, so whether {(fresh.Length == 1 ? "it is" : "they are")} gone was not checked";
            return [];
        }

        HashSet<string> remaining = new(left, StringComparer.Ordinal);
        return [.. fresh.Select(container => Swept(
            container,
            remaining.Contains(container.Id) ? SurvivorOutcome.NotStopped : container.State == "running" ? SurvivorOutcome.Stopped : SurvivorOutcome.Exited))];
    }

    private static SweptProcess Swept(Listed container, SurvivorOutcome outcome) =>
        new(0, container.Image, container.Command, User: null, outcome) { Kind = SurvivorKind.Container, Container = container.Id };

    private static async Task<IReadOnlyList<string>?> IdsAsync(DockerRun docker, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        ProcessResult listing = await docker(["ps", "-aq", "--no-trunc"], timeLimit, cancellationToken);
        return listing.ExitCode != 0 || listing.TimedOut
            ? null
            : listing.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static async Task<ProcessResult> RunAsync(string docker, IReadOnlyList<string> arguments, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        try
        {
            return await ProcessRunner.RunAsync(docker, arguments, Path.GetTempPath(), timeLimit, cancellationToken);
        }
        catch (ExecutableNotFoundException)
        {
            return new ProcessResult(-1, string.Empty, string.Empty, 0, TimedOut: false);
        }
    }

    private sealed record Listed(string Id, string Image, string State, string Command);
}
