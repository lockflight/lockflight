namespace Lockflight.Cli.Processes;

/// <summary>Starts a process and waits for it; <see cref="ProcessRunner.RunAsync(string, IEnumerable{string}, string, CancellationToken)"/> unless a test replaces it.</summary>
internal delegate Task<ProcessResult> RunProcess(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken);
