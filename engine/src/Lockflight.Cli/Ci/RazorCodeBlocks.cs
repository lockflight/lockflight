namespace Lockflight.Cli.Ci;

/// <summary>
/// The lines of a Razor file (<c>.razor</c>, <c>.cshtml</c>) that hold C# logic: those inside an <c>@code { … }</c> or
/// <c>@functions { … }</c> block, or a <c>@{ … }</c> block spanning more than one line. Those lines are production code for
/// red/green and spec/trace; the rest is markup, which gets the gates and the review only. Braces are matched without a Razor
/// parser (strings, characters and comments skipped); an unclosed block runs to the end of the file, so doubt counts as code.
/// </summary>
internal static class RazorCodeBlocks
{
    private static readonly string[] Keywords = ["code", "functions"];

    /// <summary>The 1-based numbers of the lines that hold C# logic.</summary>
    public static IReadOnlySet<int> Lines(string text)
    {
        HashSet<int> lines = [];
        int index = 0;
        while ((index = text.IndexOf('@', index)) >= 0)
        {
            int open = OpeningBrace(text, index + 1, out bool keyword);
            if (open < 0)
            {
                index++;
                continue;
            }

            int close = ClosingBrace(text, open);
            int first = LineOf(text, index);
            int last = LineOf(text, close);
            if (keyword || last > first)
            {
                for (int line = first; line <= last; line++)
                {
                    lines.Add(line);
                }
            }

            index = close + 1;
        }

        return lines;
    }

    /// <summary>The position of the brace that opens a block after an <c>@</c> at <paramref name="start"/> - 1; -1 when none does.</summary>
    private static int OpeningBrace(string text, int start, out bool keyword)
    {
        keyword = false;
        if (start < text.Length && text[start] == '{')
        {
            return start;
        }

        foreach (string word in Keywords)
        {
            if (string.CompareOrdinal(text, start, word, 0, word.Length) != 0)
            {
                continue;
            }

            int position = start + word.Length;
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }

            if (position < text.Length && text[position] == '{')
            {
                keyword = true;
                return position;
            }
        }

        return -1;
    }

    /// <summary>The position of the brace that closes the one at <paramref name="open"/>; the last character when it is never closed.</summary>
    private static int ClosingBrace(string text, int open)
    {
        int depth = 0;
        for (int position = open; position < text.Length; position++)
        {
            char current = text[position];
            char next = position + 1 < text.Length ? text[position + 1] : '\0';
            if (current is '"' or '\'')
            {
                position = EndOfLiteral(text, position, current);
            }
            else if (current == '/' && next == '/')
            {
                position = text.IndexOf('\n', position) is int end and >= 0 ? end : text.Length - 1;
            }
            else if (current == '/' && next == '*')
            {
                position = text.IndexOf("*/", position + 2, StringComparison.Ordinal) is int end and >= 0 ? end + 1 : text.Length - 1;
            }
            else if (current == '{')
            {
                depth++;
            }
            else if (current == '}' && --depth == 0)
            {
                return position;
            }
        }

        return Math.Max(text.Length - 1, open);
    }

    private static int EndOfLiteral(string text, int start, char quote)
    {
        for (int position = start + 1; position < text.Length; position++)
        {
            if (text[position] == '\\')
            {
                position++;
            }
            else if (text[position] == quote || text[position] == '\n')
            {
                return position;
            }
        }

        return text.Length - 1;
    }

    private static int LineOf(string text, int position) => 1 + text.AsSpan(0, Math.Min(position, text.Length)).Count('\n');
}
