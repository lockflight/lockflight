using System.Text;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Text that is data, not commands: POSIX here-document bodies (<c>&lt;&lt;EOF … EOF</c>) and PowerShell here-strings
/// (<c>@' … '@</c>). Positions are indexes into the command; every method returns where reading continues.
/// </summary>
internal static class ShellHereText
{
    private const char Newline = '\n';

    /// <summary>Skips the bodies of the here-documents opened on the line that just ended, in order.</summary>
    public static int SkipHereDocumentBodies(string command, int index, IReadOnlyList<(string Delimiter, bool StripsTabs)> hereDocuments)
    {
        foreach ((string delimiter, bool stripsTabs) in hereDocuments)
        {
            index = ReadHereDocumentBody(command, index, delimiter, stripsTabs).End;
        }

        return index;
    }

    /// <summary>The body of a here-document starting at the index (leading tabs removed for <c>&lt;&lt;-</c>), and where it ends.</summary>
    public static (string Body, int End) ReadHereDocumentBody(string command, int index, string delimiter, bool stripsTabs)
    {
        StringBuilder body = new();
        while (index < command.Length)
        {
            int lineEnd = command.IndexOf(Newline, index);
            string line = command[index..(lineEnd < 0 ? command.Length : lineEnd)].TrimEnd('\r');
            index = lineEnd < 0 ? command.Length : lineEnd + 1;
            string text = stripsTabs ? line.TrimStart('\t') : line;
            if (text == delimiter)
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
}
