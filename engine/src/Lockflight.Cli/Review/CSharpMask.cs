namespace Lockflight.Cli.Review;

/// <summary>
/// Blanks the parts of C# source that are not code: comments, preprocessor lines, and the contents of string and character
/// literals (their delimiters stay). The result has the same length and line breaks as the source, so braces, semicolons and
/// positions found in it are those of the code. Interpolation holes (in regular, verbatim and raw strings, across lines where C#
/// allows it) are part of their string, and the strings, characters and comments nested in a hole are skipped with it, so a quote
/// or a comment opener inside a hole never ends the string or starts a comment. It is a light scan, not a parser; the suppressions
/// step, risk routing and the judge's production-change check read code through it, so a slip can hide code from them.
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

        if (current is '"' or '$' or '@' && StringStart(source, position) is StringKind kind)
        {
            (int opening, int closing, int end) = StringSpan(source, kind);
            Contents(source, masked, opening, closing, literals);
            return end;
        }

        return position + 1;
    }

    /// <summary>Where a string literal starting at <paramref name="position"/> has its first quote, and its kind; null when none starts there.</summary>
    private static StringKind? StringStart(string source, int position)
    {
        if (position > 0 && (char.IsLetterOrDigit(source[position - 1]) || source[position - 1] == '_'))
        {
            return null;
        }

        int quote = position;
        bool verbatim = false;
        int dollars = 0;
        while (quote < source.Length && source[quote] is '$' or '@')
        {
            verbatim |= source[quote] == '@';
            dollars += source[quote] == '$' ? 1 : 0;
            quote++;
        }

        return quote < source.Length && source[quote] == '"' ? new StringKind(quote, verbatim, dollars) : null;
    }

    /// <summary>The string's contents (start, end exclusive) and where the literal ends.</summary>
    private static (int Opening, int Closing, int End) StringSpan(string source, StringKind kind)
    {
        // A raw string never takes @: @"""" is a verbatim string holding one quote.
        int quotes = kind.Verbatim ? 1 : QuoteRun(source, kind.Quote);
        int end = quotes >= 3 ? RawEnd(source, kind.Quote, quotes, kind.Dollars)
            : kind.Verbatim ? VerbatimEnd(source, kind.Quote, kind.Dollars > 0)
            : RegularEnd(source, kind.Quote, kind.Dollars > 0);
        int opening = quotes >= 3 ? kind.Quote + quotes : kind.Quote + 1;
        bool closed = quotes >= 3 ? end - quotes >= opening && string.CompareOrdinal(source, end - quotes, new string('"', quotes), 0, quotes) == 0
            : end > opening && source[end - 1] == '"';
        int closing = closed ? end - (quotes >= 3 ? quotes : 1) : end;
        return (opening, Math.Max(opening, closing), end);
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

    /// <summary>
    /// The end of a raw string: in an interpolated one, a run of at least <paramref name="dollars"/> braces opens a hole with its
    /// last <paramref name="dollars"/> braces (the ones before it are text), closed by as many.
    /// </summary>
    private static int RawEnd(string source, int quote, int quotes, int dollars)
    {
        string delimiter = new('"', quotes);
        int position = quote + quotes;
        while (position < source.Length)
        {
            if (source[position] == '"' && string.CompareOrdinal(source, position, delimiter, 0, quotes) == 0)
            {
                return position + quotes;
            }

            if (dollars > 0 && source[position] == '{')
            {
                int run = Run(source, position, '{');
                position = run >= dollars ? HoleEnd(source, position + run, dollars) : position + run;
                continue;
            }

            position++;
        }

        return source.Length;
    }

    private static int VerbatimEnd(string source, int quote, bool interpolated)
    {
        int position = quote + 1;
        while (position < source.Length)
        {
            char current = source[position];
            if (current == '"')
            {
                if (At(source, position + 1) != '"')
                {
                    return position + 1;
                }

                position += 2;
                continue;
            }

            if (interpolated && current == '{')
            {
                position = At(source, position + 1) == '{' ? position + 2 : HoleEnd(source, position + 1, 1);
                continue;
            }

            position++;
        }

        return source.Length;
    }

    /// <summary>The end of a regular string: its text cannot span lines, an interpolation hole can.</summary>
    private static int RegularEnd(string source, int quote, bool interpolated)
    {
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
                position = At(source, position + 1) == '{' ? position + 2 : HoleEnd(source, position + 1, 1);
                continue;
            }

            if (current == '"')
            {
                return position + 1;
            }

            position++;
        }

        return Math.Min(position, source.Length);
    }

    /// <summary>
    /// The end of an interpolation hole whose code starts at <paramref name="start"/>: just past the <paramref name="closing"/>
    /// braces that close it. Braces of the code nest; strings, characters and comments in it are skipped whole.
    /// </summary>
    private static int HoleEnd(string source, int start, int closing)
    {
        int depth = 0;
        int position = start;
        while (position < source.Length)
        {
            char current = source[position];
            if (current == '/' && At(source, position + 1) == '/')
            {
                position = LineEnd(source, position);
            }
            else if (current == '/' && At(source, position + 1) == '*')
            {
                int close = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
                position = close < 0 ? source.Length : close + 2;
            }
            else if (current == '\'')
            {
                position = CharacterEnd(source, position);
            }
            else if (current is '"' or '$' or '@' && StringStart(source, position) is StringKind kind)
            {
                position = StringSpan(source, kind).End;
            }
            else if (current == '{')
            {
                depth++;
                position++;
            }
            else if (current == '}' && depth > 0)
            {
                depth--;
                position++;
            }
            else if (current == '}')
            {
                return position + Math.Min(closing, Run(source, position, '}'));
            }
            else
            {
                position++;
            }
        }

        return source.Length;
    }

    private static int Run(string source, int start, char character)
    {
        int end = start;
        while (end < source.Length && source[end] == character)
        {
            end++;
        }

        return end - start;
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

    /// <summary>A string literal's first quote, whether it is verbatim, and how many <c>$</c> it has (0 when not interpolated).</summary>
    private sealed record StringKind(int Quote, bool Verbatim, int Dollars);
}
