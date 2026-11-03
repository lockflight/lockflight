using System.Text;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Text that is data, not commands: POSIX here-document bodies (<c>&lt;&lt;EOF … EOF</c>) and PowerShell here-strings
/// (<c>@' … '@</c>). Positions are indexes into the command; every method returns where reading continues.
/// </summary>
internal static class ShellHereText
{
    private const char Newline = '\n';

    /// <summary>
    /// The body of a here-document starting at the index (leading tabs removed for <c>&lt;&lt;-</c>), and where it ends. With
    /// <paramref name="joinsLines"/> (an unquoted delimiter) a line ending in an unescaped backslash is joined to the next one first.
    /// </summary>
    public static (string Body, int End) ReadHereDocumentBody(string command, int index, IReadOnlyList<string> delimiters, bool stripsTabs, bool joinsLines)
    {
        StringBuilder body = new();
        while (index < command.Length)
        {
            (string line, index) = ReadBodyLine(command, index, joinsLines);
            string text = stripsTabs ? line.TrimStart('\t') : line;
            if (delimiters.Contains(text))
            {
                break;
            }

            body.Append(text).Append(Newline);
        }

        return (body.ToString(), index);
    }

    /// <summary>Whether a PowerShell here-string (<c>@'</c> or <c>@"</c> followed by the end of the line) starts at the index.</summary>
    public static bool StartsHereString(string command, int index)
    {
        if (index + 1 >= command.Length || command[index] != '@' || command[index + 1] is not ('\'' or '"'))
        {
            return false;
        }

        int position = index + 2;
        while (position < command.Length && command[position] is ' ' or '\t' or '\r')
        {
            position++;
        }

        return position < command.Length && command[position] == Newline;
    }

    /// <summary>The body of the here-string starting at the index, and the index after its closing <c>'@</c>.</summary>
    public static (string Body, int End) ReadHereString(string command, int index)
    {
        int bodyStart = command.IndexOf(Newline, index) + 1;
        string terminator = $"{Newline}{command[index + 1]}@";
        int end = command.IndexOf(terminator, bodyStart - 1, StringComparison.Ordinal);
        int bodyEnd = end < 0 ? command.Length : end;
        return (command[bodyStart..Math.Max(bodyStart, bodyEnd)], end < 0 ? command.Length : end + terminator.Length);
    }

    private static (string Line, int End) ReadBodyLine(string command, int index, bool joinsLines)
    {
        StringBuilder line = new();
        while (true)
        {
            int lineEnd = command.IndexOf(Newline, index);
            string physical = command[index..(lineEnd < 0 ? command.Length : lineEnd)];
            index = lineEnd < 0 ? command.Length : lineEnd + 1;

            // Only a backslash right before the newline joins: before a carriage return it escapes that character.
            if (!joinsLines || lineEnd < 0 || !EndsInAnEscape(physical))
            {
                return (line.Append(physical.TrimEnd('\r')).ToString(), index);
            }

            line.Append(physical, 0, physical.Length - 1);
        }
    }

    /// <summary>Whether the line ends in a backslash that is not itself escaped (an odd number of them).</summary>
    private static bool EndsInAnEscape(string line) => (line.Length - line.TrimEnd('\\').Length) % 2 == 1;
}
