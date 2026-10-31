namespace Lockflight.Cli.Review;

/// <summary>
/// Blanks the parts of C# source that are not code: comments, preprocessor lines, and the contents of string and character
/// literals (their delimiters stay). The result has the same length and line breaks as the source, so braces, semicolons and
/// positions found in it are those of the code. It is a light scan, not a parser: unusual nesting inside interpolations may
/// blank too little or too much, which only makes a signature listing less complete.
/// </summary>
internal static class CSharpMask
{
    public static string Mask(string source) => Scan(source, literals: null);

    /// <summary>
    /// The source with comments and preprocessor lines blanked but string and character literals kept whole; adds the span
    /// (start, end exclusive) of every literal's contents to <paramref name="literals"/>, its line breaks included.
    /// </summary>
    public static string MaskComments(string source, List<(int Start, int End)> literals) => Scan(source, literals);

    private static string Scan(string source, List<(int Start, int End)>? literals)
    {
        char[] masked = source.ToCharArray();
        int position = 0;
        while (position < source.Length)
        {
            position = Next(source, masked, position, literals);
        }

        return new string(masked);
    }

    private static int Next(string source, char[] masked, int position, List<(int Start, int End)>? literals)
    {
        char current = source[position];
        if (current == '/' && At(source, position + 1) == '/')
        {
            return Blank(source, masked, position, LineEnd(source, position));
        }

        if (current == '/' && At(source, position + 1) == '*')
        {
            int close = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
            return Blank(source, masked, position, close < 0 ? source.Length : close + 2);
        }

        if (current == '#' && IsLineStart(source, position))
        {
            return Blank(source, masked, position, LineEnd(source, position));
        }

        if (current == '\'')
        {
            return Literal(source, masked, position, CharacterEnd(source, position), literals);
        }

        if (current is '"' or '$' or '@' && StringStart(source, position) is (int quote, bool verbatim, bool interpolated))
        {
            int quotes = QuoteRun(source, quote);
            int end = quotes >= 3 ? RawEnd(source, quote, quotes)
                : verbatim ? VerbatimEnd(source, quote)
                : RegularEnd(source, quote, interpolated);
            int opening = quotes >= 3 ? quote + quotes : quote + 1;
            int closing = quotes >= 3 ? Math.Max(opening, end - quotes) : Math.Max(opening, end - 1);
            Contents(source, masked, opening, closing, literals);
            return end;
        }

        return position + 1;
    }

    /// <summary>Where a string literal starting at <paramref name="position"/> has its first quote, and its kind; null when none starts there.</summary>
    private static (int Quote, bool Verbatim, bool Interpolated)? StringStart(string source, int position)
    {
        if (position > 0 && (char.IsLetterOrDigit(source[position - 1]) || source[position - 1] == '_'))
        {
            return null;
        }

        int quote = position;
        bool verbatim = false;
        bool interpolated = false;
        while (quote < source.Length && source[quote] is '$' or '@')
        {
            verbatim |= source[quote] == '@';
            interpolated |= source[quote] == '$';
            quote++;
        }

        return quote < source.Length && source[quote] == '"' ? (quote, verbatim, interpolated) : null;
    }

    private static int QuoteRun(string source, int start)
    {
        int end = start;
        while (end < source.Length && source[end] == '"')
        {
            end++;
        }

        int run = end - start;
        // "" is an empty string, not the start of a raw string literal.
        return run == 2 ? 1 : run;
    }

    private static int RawEnd(string source, int quote, int quotes)
    {
        string delimiter = new('"', quotes);
        int close = source.IndexOf(delimiter, quote + quotes, StringComparison.Ordinal);
        return close < 0 ? source.Length : close + quotes;
    }

    private static int VerbatimEnd(string source, int quote)
    {
        int position = quote + 1;
        while (position < source.Length)
        {
            if (source[position] == '"')
            {
                if (At(source, position + 1) != '"')
                {
                    return position + 1;
                }

                position++;
            }

            position++;
        }

        return source.Length;
    }

    private static int RegularEnd(string source, int quote, bool interpolated)
    {
        int holes = 0;
        int position = quote + 1;
        while (position < source.Length && source[position] != '\n')
        {
            char current = source[position];
            if (current == '\\')
            {
                position += 2;
                continue;
            }

            if (interpolated && current == '{')
            {
                if (holes == 0 && At(source, position + 1) == '{')
                {
                    position += 2;
                    continue;
                }

                holes++;
            }
            else if (interpolated && current == '}' && holes > 0)
            {
                holes--;
            }
            else if (current == '"' && holes > 0)
            {
                position = RegularEnd(source, position, interpolated: false);
                continue;
            }
            else if (current == '"')
            {
                return position + 1;
            }

            position++;
        }

        return position;
    }

    private static int CharacterEnd(string source, int quote)
    {
        int position = quote + 1;
        while (position < source.Length && source[position] is not ('\'' or '\n'))
        {
            position += source[position] == '\\' ? 2 : 1;
        }

        return Math.Min(source.Length, position + 1);
    }

    private static int Literal(string source, char[] masked, int start, int end, List<(int Start, int End)>? literals)
    {
        Contents(source, masked, start + 1, Math.Max(start + 1, end - 1), literals);
        return end;
    }

    /// <summary>Blanks a literal's contents, or, when the caller collects literals, keeps them and records their span.</summary>
    private static void Contents(string source, char[] masked, int start, int end, List<(int Start, int End)>? literals)
    {
        if (literals is null)
        {
            Blank(source, masked, start, end);
        }
        else if (end > start)
        {
            literals.Add((start, Math.Min(end, source.Length)));
        }
    }

    private static int Blank(string source, char[] masked, int start, int end)
    {
        for (int index = start; index < end && index < source.Length; index++)
        {
            if (source[index] is not ('\n' or '\r'))
            {
                masked[index] = ' ';
            }
        }

        return Math.Max(end, start + 1);
    }

    private static bool IsLineStart(string source, int position)
    {
        for (int index = position - 1; index >= 0 && source[index] != '\n'; index--)
        {
            if (!char.IsWhiteSpace(source[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static int LineEnd(string source, int position)
    {
        int end = source.IndexOf('\n', position);
        return end < 0 ? source.Length : end;
    }

    private static char At(string source, int position) => position < source.Length ? source[position] : '\0';
}
