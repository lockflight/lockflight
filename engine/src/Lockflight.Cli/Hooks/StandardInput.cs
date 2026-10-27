namespace Lockflight.Cli.Hooks;

/// <summary>
/// What a command reads on stdin, as far as the command line shows it: <see cref="Text"/> written out (a here-document, a
/// here-string, the words of an <c>echo</c> piped into it), or <see cref="Hidden"/> naming a command whose output the guard cannot
/// read (<c>cat x.sh |</c>, <c>curl … |</c>). Both are null when nothing visible feeds it.
/// </summary>
internal sealed record StandardInput(string? Text, string? Hidden)
{
    public static StandardInput None { get; } = new(null, null);

    /// <summary>The input a command gets from the simple command before the pipe; null when that was a compound command.</summary>
    public static StandardInput Piped(ShellCommand? upstream)
    {
        if (upstream is null)
        {
            return new(null, "a compound command");
        }

        IReadOnlyList<string> arguments = upstream.Arguments;
        return upstream.Program switch
        {
            "echo" => new(Unescape(string.Join(' ', arguments.SkipWhile(argument => argument is "-n" or "-e" or "-E" or "-ne" or "-en"))), null),
            "printf" when arguments.Count == 1 && !arguments[0].Contains('%', StringComparison.Ordinal) => new(Unescape(arguments[0]), null),
            _ => new(null, upstream.Program),
        };
    }

    /// <summary><c>\n</c> as <c>printf</c> and <c>echo -e</c> write it; reading it as a line break can only split more commands out.</summary>
    private static string Unescape(string text) => text.Replace("\\n", "\n", StringComparison.Ordinal);
}
