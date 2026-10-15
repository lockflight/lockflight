using System.Text;

namespace Lockflight.Cli.Hooks;

/// <summary>What an escape character yields: the text it adds to the current word and how many characters it consumes.</summary>
internal static class ShellEscapes
{
    private const char Newline = '\n';

    /// <summary>Outside quotes the escape takes the next character literally; before a newline it continues the line.</summary>
    public static (string Text, int Length) Unquoted(string command, int index)
    {
        char escape = command[index];
        if (index + 1 >= command.Length)
        {
            return (escape.ToString(), 1);
        }

        char next = command[index + 1];
        if (next == Newline)
        {
            return (string.Empty, 2);
        }

        return next == '\r' && index + 2 < command.Length && command[index + 2] == Newline ? (string.Empty, 3) : (next.ToString(), 2);
    }

    /// <summary>
    /// Inside double quotes a POSIX backslash escapes only <c>$</c>, <c>`</c>, <c>"</c>, <c>\</c> and newline and is kept before
    /// anything else (<c>"C:\repo"</c> stays <c>C:\repo</c>); a PowerShell backtick escapes any character.
    /// </summary>
    public static (string Text, int Length) InDoubleQuotes(string command, int index, ShellDialect dialect)
    {
        char escape = command[index];
        char next = command[index + 1];
        if (next == Newline)
        {
            return (string.Empty, 2);
        }

        bool keepsEscape = dialect == ShellDialect.Posix && next is not ('$' or '`' or '"' or '\\');
        return (keepsEscape ? $"{escape}{next}" : next.ToString(), 2);
    }

    /// <summary>
    /// Bash ANSI-C quoting <c>$'…'</c> starting at the index: the text with <c>\n</c>, <c>\t</c>, <c>\\</c>, <c>\'</c>,
    /// <c>\"</c>, <c>\xHH</c> and octal <c>\NNN</c> decoded (other escapes keep their character).
    /// </summary>
    public static (string Text, int Length) AnsiC(string command, int index)
    {
        StringBuilder text = new();
        int position = index + 2;
        while (position < command.Length && command[position] != '\'')
        {
            if (command[position] != '\\' || position + 1 >= command.Length)
            {
                text.Append(command[position++]);
                continue;
            }

            char code = command[position + 1];
            position += 2;
            if (code is 'x' && HexDigits(command, position, 2) is (int hexValue, int hexLength) && hexLength > 0)
            {
                text.Append((char)hexValue);
                position += hexLength;
            }
            else if (code is >= '0' and <= '7')
            {
                int octalEnd = position - 1;
                while (octalEnd < command.Length && octalEnd < position + 2 && command[octalEnd] is >= '0' and <= '7')
                {
                    octalEnd++;
                }

                text.Append((char)Convert.ToInt32(command[(position - 1)..octalEnd], 8));
                position = octalEnd;
            }
            else
            {
                text.Append(code switch { 'n' => '\n', 't' => '\t', 'r' => '\r', 'a' => '\a', 'e' or 'E' => '\u001b', _ => code });
            }
        }

        return (text.ToString(), Math.Min(command.Length, position + 1) - index);
    }

    private static (int Value, int Length) HexDigits(string command, int start, int maxLength)
    {
        int value = 0;
        int length = 0;
        while (length < maxLength && start + length < command.Length && Uri.IsHexDigit(command[start + length]))
        {
            value = (value * 16) + Uri.FromHex(command[start + length]);
            length++;
        }

        return (value, length);
    }
}
