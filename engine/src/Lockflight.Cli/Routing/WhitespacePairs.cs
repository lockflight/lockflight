namespace Lockflight.Cli.Routing;

/// <summary>
/// Drops reformatting from a file's diff: a removed line and an added line that are equal once runs of whitespace are collapsed
/// and the ends trimmed are paired and both dropped, in order, so a reindent, a re-wrap of spaces or a CRLF normalization counts
/// as nothing and fires no signal. The text is compared as written (not masked), so editing a string literal or a comment that
/// shares a line with code is still a change.
/// </summary>
internal static class WhitespacePairs
{
    public static (IReadOnlyList<RoutedLine> Added, IReadOnlyList<RoutedLine> Removed) Drop(IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed)
    {
        Dictionary<string, Queue<int>> removedByText = new(StringComparer.Ordinal);
        for (int index = 0; index < removed.Count; index++)
        {
            string key = Collapse(removed[index].Text);
            if (!removedByText.TryGetValue(key, out Queue<int>? queue))
            {
                queue = new Queue<int>();
                removedByText[key] = queue;
            }

            queue.Enqueue(index);
        }

        HashSet<int> pairedRemoved = [];
        List<RoutedLine> keptAdded = [];
        foreach (RoutedLine line in added)
        {
            if (removedByText.TryGetValue(Collapse(line.Text), out Queue<int>? queue) && queue.TryDequeue(out int match))
            {
                pairedRemoved.Add(match);
            }
            else
            {
                keptAdded.Add(line);
            }
        }

        return (keptAdded, [.. removed.Where((_, index) => !pairedRemoved.Contains(index))]);
    }

    /// <summary>The text with every run of whitespace as one space and none at the ends.</summary>
    public static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
