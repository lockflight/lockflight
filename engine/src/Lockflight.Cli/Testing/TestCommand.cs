using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Testing;

/// <summary>A <c>dotnet</c> command line and the directory it runs in, printed so a person can paste it into a POSIX shell (or Git Bash).</summary>
internal sealed record TestCommand(string WorkingDirectory, IReadOnlyList<string> Arguments)
{
    /// <summary><c>cd '&lt;directory&gt;' &amp;&amp; dotnet …</c>, every word quoted where the shell would split or expand it.</summary>
    public string Line => $"cd {ShellQuoting.QuoteIfNeeded(WorkingDirectory)} && dotnet {string.Join(' ', Arguments.Select(ShellQuoting.QuoteIfNeeded))}";
}
