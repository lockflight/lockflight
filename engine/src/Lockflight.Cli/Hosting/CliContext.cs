using Lockflight.Cli.Linking;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Processes.Survivors;

namespace Lockflight.Cli.Hosting;

/// <summary>The process boundary a command works against, replaceable in tests.</summary>
internal sealed record CliContext(TextReader Input, TextWriter Output, TextWriter Error, string WorkingDirectory, TimeProvider Time)
{
    /// <summary>The environment variables the engine reads; the process environment unless a test supplies its own.</summary>
    public EnvironmentReader Environment { get; init; } = EnvironmentReader.Process;

    /// <summary>One-time codes; cryptographically secure unless a test fixes them.</summary>
    public RandomSource Random { get; init; } = RandomSource.Secure;

    /// <summary>Starts the processes a command hands work to (the .NET SDK, git, a newer engine); real ones unless a test fakes them.</summary>
    public RunProcess Processes { get; init; } = ProcessRunner.RunAsync;

    /// <summary>Starts Claude Code for the review (<c>claude -p</c> with the prompt on stdin, <c>claude --version</c>); real unless a test fakes it.</summary>
    public RunProcessWithInput Claude { get; init; } = ProcessRunner.RunAsync;

    /// <summary>The engine binary <c>lockflight link</c> copies into a project; this process unless a test supplies one.</summary>
    public RunningEngine Engine { get; init; } = RunningEngine.Current;

    /// <summary>
    /// Sets up the CI judge's sweep for processes left running (<see cref="ProcessScopes.Begin"/>). Off unless the host turns it on:
    /// it changes the process it runs in for good (a Linux subreaper, a Windows job), which a test host must not have.
    /// </summary>
    /// <remarks>The argument says whether the run is a CI job's (<see cref="ProcessScopes.Begin"/>).</remarks>
    public Func<bool, IProcessScope> ProcessScope { get; init; } = ProcessScopes.Off;

    /// <summary>Whether <see cref="Output"/> is a terminal a person reads (not a pipe or a file); false unless the host says so.</summary>
    public bool OutputIsTerminal { get; init; }

    /// <summary>
    /// <see cref="Output"/> without <see cref="TerminalSafeWriter"/>, for the engine's own escape sequences only (Levi's colours);
    /// text from the project never goes here unless <see cref="TerminalText.Clean(string)"/> has made it printable. Null means
    /// <see cref="Output"/> is not wrapped.
    /// </summary>
    public TextWriter? RawOutput { get; init; }

    /// <summary>This context with standard output and error passed through <see cref="TerminalSafeWriter"/>.</summary>
    public CliContext ForTerminal() => RawOutput is not null
        ? this
        : this with { Output = new TerminalSafeWriter(Output), Error = new TerminalSafeWriter(Error), RawOutput = Output };
}
