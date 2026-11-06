namespace Lockflight.Cli.Processes.Survivors;

/// <summary>
/// The Docker containers created while the CI judge runs. The runner's user can start a container without <c>sudo</c>, and a
/// container is not a descendant of the engine (the Docker daemon starts it), so the process sweep does not see it. The judge
/// lists the containers when it starts and, at the end of each part of its run, finds the ones created since, removes them
/// (<c>docker rm -f</c>) and records them; one that could not be removed is not stopped. A local run, or one on a self-hosted
/// runner whose Docker daemon other jobs may share, lists them and leaves them. A guardrail: a container that is created and
/// gone between two checks is not seen.
/// </summary>
internal sealed class DockerContainers(string docker, IReadOnlySet<string> before, bool remove)
{
    private const char Separator = '\t';
    private const int ListedFields = 4;
    private static readonly TimeSpan FirstAnswerTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ListTime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RemoveTime = TimeSpan.FromSeconds(60);

    private readonly HashSet<string> seen = [.. before];

    /// <summary>
    /// Lists the containers that exist now; null when there is no <c>docker</c> on PATH or its daemon does not answer within 2
    /// seconds, so there is nothing to check.
    /// </summary>
    public static async Task<DockerContainers?> WatchAsync(bool remove, CancellationToken cancellationToken)
    {
        if (Executables.Find("docker", explicitPath: null) is not string docker)
        {
            return null;
        }

        IReadOnlyList<string>? ids = await IdsAsync(docker, FirstAnswerTime, cancellationToken);
        return ids is null ? null : new DockerContainers(docker, new HashSet<string>(ids, StringComparer.Ordinal), remove);
    }

    /// <summary>The containers created since the judge started and not yet reported, removed unless this run only lists them.</summary>
    public async Task<IReadOnlyList<SweptProcess>> SweepAsync(CancellationToken cancellationToken)
    {
        ProcessResult listing = await RunAsync(docker, ["ps", "-a", "--no-trunc", "--format", "{{.ID}}\t{{.Image}}\t{{.State}}\t{{.Command}}"], ListTime, cancellationToken);
        if (listing.ExitCode != 0 || listing.TimedOut)
        {
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

        await RunAsync(docker, ["rm", "-f", .. fresh.Select(container => container.Id)], RemoveTime, cancellationToken);
        IReadOnlyList<string>? left = await IdsAsync(docker, ListTime, cancellationToken);
        HashSet<string> remaining = left is null ? [.. fresh.Select(container => container.Id)] : new HashSet<string>(left, StringComparer.Ordinal);
        return [.. fresh.Select(container => Swept(
            container,
            remaining.Contains(container.Id) ? SurvivorOutcome.NotStopped : container.State == "running" ? SurvivorOutcome.Stopped : SurvivorOutcome.Exited))];
    }

    private static SweptProcess Swept(Listed container, SurvivorOutcome outcome) =>
        new(0, container.Image, container.Command, User: null, outcome) { Kind = SurvivorKind.Container, Container = container.Id };

    private static async Task<IReadOnlyList<string>?> IdsAsync(string docker, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        ProcessResult listing = await RunAsync(docker, ["ps", "-aq", "--no-trunc"], timeLimit, cancellationToken);
        return listing.ExitCode != 0 || listing.TimedOut
            ? null
            : listing.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static async Task<ProcessResult> RunAsync(string docker, IEnumerable<string> arguments, TimeSpan timeLimit, CancellationToken cancellationToken)
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
