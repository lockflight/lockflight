namespace Lockflight.Cli.Processes.Survivors;

/// <summary>Runs the <c>docker</c> client with <paramref name="arguments"/>, killed after <paramref name="timeLimit"/>.</summary>
internal delegate Task<ProcessResult> DockerRun(IReadOnlyList<string> arguments, TimeSpan timeLimit, CancellationToken cancellationToken);
