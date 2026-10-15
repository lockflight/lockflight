namespace Lockflight.Cli.Processes;

/// <summary>
/// Outcome of a child process: exit code, standard output, standard error, wall time, and whether it was killed for running too
/// long. Data is parsed from <see cref="Output"/> only; <see cref="Combined"/> is for messages and for recognising tool problems.
/// </summary>
internal sealed record ProcessResult(int ExitCode, string Output, string Error, long DurationMs, bool TimedOut)
{
    /// <summary>Standard output followed by standard error, for messages a person reads.</summary>
    public string Combined => Error.Length == 0 ? Output : Output.Length == 0 ? Error : $"{Output}\n{Error}";
}
