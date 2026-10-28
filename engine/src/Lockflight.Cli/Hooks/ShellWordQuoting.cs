using System.Buffers;
using System.Text;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Writes parsed words back into a POSIX command line that splits into the same words. Wildcards stay unquoted, so a target is
/// still checked as the entries it matches as well as written, and the placeholder of a command substitution
/// (<see cref="ShellTokenizer.SubstitutionPlaceholder"/>) stays an expansion, so a computed word stays computed.
/// </summary>
internal static class ShellWordQuoting
{
    private const string EmptyWord = "\"\"";
    private const string QuotedNewline = "\"\n\"";

    private static readonly SearchValues<char> Escaped = SearchValues.Create(" \t\"'$`\\;&|<>()#~!=");

    public static string Line(IEnumerable<string> words) => string.Join(' ', words.Select(Word));

    public static string Word(string word)
    {
        if (word.Length == 0)
        {
            return EmptyWord;
        }

        StringBuilder quoted = new();
        for (int index = 0; index < word.Length; index++)
        {
            char character = word[index];
            if (word.AsSpan(index).StartsWith(ShellTokenizer.SubstitutionPlaceholder, StringComparison.Ordinal))
            {
                quoted.Append(ShellTokenizer.SubstitutionPlaceholder);
                index += ShellTokenizer.SubstitutionPlaceholder.Length - 1;
            }
            else if (character == '\n')
            {
                quoted.Append(QuotedNewline);
            }
            else
            {
                quoted.Append(Escaped.Contains(character) ? $"\\{character}" : character.ToString());
            }
        }

        return quoted.ToString();
    }
}
