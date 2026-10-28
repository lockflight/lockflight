namespace Lockflight.Cli.Docs;

/// <summary>Inline code spans of one Markdown line (<c>`x`</c>, <c>``a ` b``</c>), and the line with them blanked out.</summary>
internal static class InlineCode
{
    public static IReadOnlyList<string> Spans(string line) => [.. Find(line).Select(span => span.Content)];

    /// <summary>The line with every code span replaced by spaces, so links are not read inside code.</summary>
    public static string Blanked(string line)
    {
        char[] characters = line.ToCharArray();
        foreach ((int start, int length, _) in Find(line))
        {
            Array.Fill(characters, ' ', start, length);
        }

        return new string(characters);
    }

    private static IEnumerable<(int Start, int Length, string Content)> Find(string line)
    {
        int index = 0;
        while (index < line.Length)
        {
            if (line[index] != '`')
            {
                index++;
                continue;
            }

            int run = RunLength(line, index);
            int close = FindClosing(line, index + run, run);
            if (close < 0)
            {
                index += run;
                continue;
            }

            string content = line[(index + run)..close];
            if (content.Length >= 2 && content[0] == ' ' && content[^1] == ' ' && content.Trim().Length > 0)
            {
                content = content[1..^1];
            }

            yield return (index, close + run - index, content);
            index = close + run;
        }
    }

    private static int FindClosing(string line, int from, int run)
    {
        int index = from;
        while (index < line.Length)
        {
            if (line[index] == '`')
            {
                int length = RunLength(line, index);
                if (length == run)
                {
                    return index;
                }

                index += length;
            }
            else
            {
                index++;
            }
        }

        return -1;
    }

    private static int RunLength(string line, int index)
    {
        int end = index;
        while (end < line.Length && line[end] == '`')
        {
            end++;
        }

        return end - index;
    }
}
