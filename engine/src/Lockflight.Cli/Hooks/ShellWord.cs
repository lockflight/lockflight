namespace Lockflight.Cli.Hooks;

/// <summary>
/// A word of a simple command as the write scanner uses it: <see cref="Text"/> with the variables the command line set to literal
/// text replaced (see <see cref="ShellLiteralVariables"/>), the <see cref="Source"/> token as written, the assignment the word
/// makes, and whether it is the target of an output redirection.
/// </summary>
internal sealed record ShellWord(string Text, ShellToken Source)
{
    public ShellAssignment? Assignment { get; init; }

    public bool RedirectTarget { get; init; }
}
