namespace Lockflight.Cli.Processes.Survivors;

/// <summary>
/// A process a sweep found still running, and what became of it. <see cref="Command"/> is its command line where the platform
/// gives one (Linux), else its image; <see cref="User"/> is null where the platform does not say.
/// </summary>
internal sealed record SweptProcess(int Pid, string Image, string Command, string? User, SurvivorOutcome Outcome);
