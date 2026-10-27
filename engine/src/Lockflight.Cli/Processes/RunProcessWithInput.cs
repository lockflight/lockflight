namespace Lockflight.Cli.Processes;

/// <summary>
/// Starts a process with a time limit and, optionally, a file as its standard input;
/// <see cref="ProcessRunner.RunAsync(string, IEnumerable{string}, string, TimeSpan, string?, CancellationToken)"/> unless a test replaces it.
/// </summary>
internal delegate Task<ProcessResult> RunProcessWithInput(
    string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeLimit, string? standardInputPath, CancellationToken cancellationToken);
