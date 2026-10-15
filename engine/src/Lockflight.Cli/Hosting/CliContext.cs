using Lockflight.Cli.Processes;

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

    /// <summary>Whether <see cref="Output"/> is a terminal a person reads (not a pipe or a file); false unless the host says so.</summary>
    public bool OutputIsTerminal { get; init; }
}
