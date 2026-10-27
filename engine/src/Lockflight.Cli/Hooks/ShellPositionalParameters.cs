using System.Buffers;
using System.Globalization;
using System.Text;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Binds <c>$0</c>, <c>$1</c>…, <c>${N}</c>, <c>$@</c> and <c>$*</c> in a command string to the words given after it
/// (<c>sh -c 'rm "$1"' _ file</c>), as the shell that runs it would. A reference inside single quotes stays literal; one whose value
/// cannot be written into the string without changing how it splits stays a reference (an unknown variable).
/// </summary>
internal static class ShellPositionalParameters
{
    private const string AllParameters = "@";
    private const string JoinedParameters = "*";

    private static readonly SearchValues<char> NeedsQuoting = SearchValues.Create(" \t\n\"'$`\\;&|<>()#~");
    private static readonly SearchValues<char> SpecialInDoubleQuotes = SearchValues.Create("\"$`\\");

    /// <summary>The command string with its positional parameters replaced; <paramref name="parameters"/> starts at <c>$0</c>.</summary>
    public static string Bind(string command, IReadOnlyList<string> parameters)
    {
        if (!command.Contains('$', StringComparison.Ordinal))
        {
            return command;
        }

        StringBuilder bound = new();
        bool doubleQuoted = false;
        for (int index = 0; index < command.Length; index++)
        {
            char character = command[index];
            if (character == '\'' && !doubleQuoted)
            {
                int end = command.IndexOf('\'', index + 1);
                end = end < 0 ? command.Length - 1 : end;
                bound.Append(command, index, end - index + 1);
                index = end;
            }
            else if (character == '\\' && index + 1 < command.Length)
            {
                bound.Append(command, index, 2);
                index++;
            }
            else if (character == '$' && Reference(command, index) is (string name, int length) && Value(name, parameters, doubleQuoted) is string value)
            {
                bound.Append(value);
                index += length - 1;
            }
            else
            {
                doubleQuoted ^= character == '"';
                bound.Append(character);
            }
        }

        return bound.ToString();
    }

    /// <summary>The parameter a <c>$</c> at the index refers to (<c>0</c>…, <c>@</c>, <c>*</c>) and the length of the reference.</summary>
    private static (string Name, int Length)? Reference(string command, int index)
    {
        char next = index + 1 < command.Length ? command[index + 1] : '\0';
        if (char.IsAsciiDigit(next) || next is '@' or '*')
        {
            return (next.ToString(), 2);
        }

        int close = next == '{' ? command.IndexOf('}', index + 2) : -1;
        if (close < 0)
        {
            return null;
        }

        string name = command[(index + 2)..close];
        bool positional = name is AllParameters or JoinedParameters || (name.Length > 0 && name.All(char.IsAsciiDigit));
        return positional ? (name, close - index + 1) : null;
    }

    private static string? Value(string name, IReadOnlyList<string> parameters, bool doubleQuoted)
    {
        if (name is AllParameters or JoinedParameters)
        {
            List<string?> values = [.. parameters.Skip(1).Select(value => Quoted(value, doubleQuoted))];
            if (values.Any(value => value is null))
            {
                return null;
            }

            // Inside double quotes "$@" is one word per parameter: close and reopen the quotes between them.
            return string.Join(doubleQuoted && name == AllParameters ? "\" \"" : " ", values);
        }

        bool given = int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int position) && position < parameters.Count;
        return Quoted(given ? parameters[position] : string.Empty, doubleQuoted);
    }

    private static string? Quoted(string value, bool doubleQuoted)
    {
        if (doubleQuoted)
        {
            return value.AsSpan().IndexOfAny(SpecialInDoubleQuotes) < 0 ? value : null;
        }

        if (value.AsSpan().IndexOfAny(NeedsQuoting) < 0)
        {
            return value;
        }

        return value.Contains('\'', StringComparison.Ordinal) ? null : $"'{value}'";
    }
}
