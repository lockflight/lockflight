namespace Lockflight.Cli.Processes;

/// <summary>
/// Outcome of a child process: exit code, standard output, standard error, wall time, and whether it was killed for running too
/// long. Data is parsed from <see cref="Output"/> only; <see cref="Combined"/> is for messages and for recognising tool problems.
/// </summary>
internal sealed record ProcessResult(int ExitCode, string Output, string Error, long DurationMs, bool TimedOut)
{
    /// <summary>
    /// The process exited, but something it started kept its output open, so the runner stopped waiting after a drain;
    /// <see cref="Output"/> and <see cref="Error"/> hold what was written until then, which may be cut anywhere. A caller that parses
    /// the output reads it through <see cref="CompleteOutput"/>, never as it is.
    /// </summary>
    public bool OutputHeld { get; init; }

    /// <summary>Standard output followed by standard error, for messages a person reads.</summary>
    public string Combined => Error.Length == 0 ? Output : Output.Length == 0 ? Error : $"{Output}\n{Error}";
}
