namespace Lockflight.Cli.Processes;

/// <summary>Outcome of a child process whose standard output is binary: exit code, the raw output bytes, standard error, and whether it was killed for running too long.</summary>
internal sealed record BinaryProcessResult(int ExitCode, byte[] Output, string Error, bool TimedOut);
