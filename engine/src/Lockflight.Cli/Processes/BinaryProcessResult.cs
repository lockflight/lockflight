namespace Lockflight.Cli.Processes;

/// <summary>Outcome of a child process whose standard output is binary: exit code, the raw output bytes, standard error, and whether it was killed for running too long.</summary>
internal sealed record BinaryProcessResult(int ExitCode, byte[] Output, string Error, bool TimedOut)
{
    /// <summary>The process exited, but something it started kept its output open; the output holds what was written until the runner stopped waiting.</summary>
    public bool OutputHeld { get; init; }
}
