namespace Lockflight.Cli.Processes;

/// <summary>A tool the engine runs is not installed or not on PATH; the message names it and what to do.</summary>
internal sealed class ExecutableNotFoundException(string fileName, Exception innerException)
    : Exception($"'{fileName}' was not found on PATH; install it (or add its directory to PATH) and run the command again.", innerException);
