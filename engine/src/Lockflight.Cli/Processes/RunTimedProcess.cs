namespace Lockflight.Cli.Processes;

/// <summary>
/// Starts a tool and waits for it at most <paramref name="timeLimit"/>, killing its process tree after that; the gate steps run
/// their tools through it, <see cref="ProcessRunner.RunAsync(string, IEnumerable{string}, string, TimeSpan, CancellationToken)"/>
/// unless a test replaces it.
/// </summary>
internal delegate Task<ProcessResult> RunTimedProcess(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken);
