using System.Globalization;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Bash brace expansion of a write target: lists (<c>secrets/{a,b}</c> → <c>secrets/a</c>, <c>secrets/b</c>) and sequences
/// (<c>{1..3}</c>, <c>{a..c}</c>, <c>{01..10..3}</c> with its zero padding). A <c>${…}</c> is a variable, not a brace list. Quoted
/// braces were already removed by the tokenizer, so a quoted <c>{a,b}</c> is expanded too: an extra path to check, never a missed
/// one.
/// </summary>
internal static partial class BraceExpansion
{
    public const int MaxExpansions = 64;

    /// <summary>The words the target expands to; null when there are more than <see cref="MaxExpansions"/>.</summary>
    public static IReadOnlyList<string>? Expand(string word)
    {
        List<string> pending = [word];
        List<string> done = [];
        while (pending.Count > 0)
        {
            string current = pending[^1];
            pending.RemoveAt(pending.Count - 1);
            if (FindList(current) is not (int open, int close, List<string> alternatives))
            {
                done.Add(current);
            }
            else
            {
                pending.AddRange(alternatives.Select(alternative => current[..open] + alternative + current[(close + 1)..]));
            }

            if (done.Count + pending.Count > MaxExpansions)
            {
                return null;
            }
        }

        return done;
    }

    /// <summary>The first brace list with a top-level comma: its braces' positions and its alternatives.</summary>
    private static (int Open, int Close, List<string> Alternatives)? FindList(string word)
    {
        for (int open = word.IndexOf('{', StringComparison.Ordinal); open >= 0; open = word.IndexOf('{', open + 1))
        {
            if (open > 0 && word[open - 1] == '$')
            {
                continue;
            }

            int depth = 0;
            int start = open + 1;
            List<string> alternatives = [];
            for (int index = open + 1; index < word.Length; index++)
            {
                char character = word[index];
                if (character == '{')
                {
                    depth++;
                }
                else if (character == '}' && depth > 0)
                {
                    depth--;
                }
                else if (character == ',' && depth == 0)
                {
                    alternatives.Add(word[start..index]);
                    start = index + 1;
                }
                else if (character == '}')
                {
                    if (alternatives.Count == 0)
                    {
                        if (Sequence(word[start..index]) is List<string> sequence)
                        {
                            return (open, index, sequence);
                        }

                        break;
                    }

                    alternatives.Add(word[start..index]);
                    return (open, index, alternatives);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The words of <c>X..Y[..STEP]</c> (integers or single letters); null when the text is no sequence. At most one more word than
    /// <see cref="MaxExpansions"/> is made, which is enough to know there are too many.
    /// </summary>
    private static List<string>? Sequence(string text)
    {
        Match match = SequencePattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        string first = match.Groups["first"].Value;
        string last = match.Groups["last"].Value;
        bool letters = char.IsAsciiLetter(first[0]) && char.IsAsciiLetter(last[0]);
        long from;
        long to;
        if (letters)
        {
            (from, to) = (first[0], last[0]);
        }
        else if (!long.TryParse(first, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out from)
            || !long.TryParse(last, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out to))
        {
            return null;
        }

        long step = match.Groups["step"].Success && long.TryParse(match.Groups["step"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long given) && given != 0
            ? Math.Abs(given)
            : 1;
        int width = letters || !(IsPadded(first) || IsPadded(last)) ? 0 : Math.Max(first.Length, last.Length);
        List<string> words = [];
        for (long value = from; (from <= to ? value <= to : value >= to) && words.Count <= MaxExpansions; value += from <= to ? step : -step)
        {
            words.Add(letters ? ((char)value).ToString() : Padded(value, width));
        }

        return words;
    }

    private static bool IsPadded(string number) => number.TrimStart('-').Length > 1 && number.TrimStart('-')[0] == '0';

    private static string Padded(long value, int width)
    {
        string digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture).PadLeft(value < 0 ? width - 1 : width, '0');
        return value < 0 ? "-" + digits : digits;
    }

    [GeneratedRegex(@"^(?<first>-?\d{1,18}|[A-Za-z])\.\.(?<last>-?\d{1,18}|[A-Za-z])(\.\.(?<step>-?\d{1,18}))?$")]
    private static partial Regex SequencePattern();
}
