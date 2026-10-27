namespace Lockflight.Cli.Hooks;

/// <summary>
/// A word of a simple command as the write scanner uses it: <see cref="Text"/> with the variables the command line set to literal
/// text replaced (see <see cref="ShellLiteralVariables"/>), the <see cref="Source"/> token as written, the assignment the word
/// makes, whether it is the target of an output redirection or the text the command reads on stdin, and the other values it may
/// stand for (<see cref="Alternatives"/>).
/// </summary>
internal sealed record ShellWord(string Text, ShellToken Source)
{
    public ShellAssignment? Assignment { get; init; }

    public bool RedirectTarget { get; init; }

    /// <summary>A here-document body or a here-string: the command reads it, it is no argument.</summary>
    public bool StandardInput { get; init; }

    /// <summary>Other texts the word may expand to, each checked as well: the word of a default (<c>${X:-word}</c>).</summary>
    public IReadOnlyList<string> Alternatives { get; init; } = [];
}
