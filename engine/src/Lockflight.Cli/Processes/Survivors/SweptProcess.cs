namespace Lockflight.Cli.Processes.Survivors;

/// <summary>
/// A process a sweep found still running, and what became of it. <see cref="Command"/> is its command line where the platform
/// gives one (Linux), else its image; <see cref="User"/> is null where the platform does not say. A Docker container has no
/// process id here (0): <see cref="Container"/> holds its id, <see cref="Image"/> its image and <see cref="Command"/> its command.
/// </summary>
internal sealed record SweptProcess(int Pid, string Image, string Command, string? User, SurvivorOutcome Outcome)
{
    public SurvivorKind Kind { get; init; } = SurvivorKind.Process;

    /// <summary>The container's id, for a <see cref="SurvivorKind.Container"/>; null for a process.</summary>
    public string? Container { get; init; }
}
