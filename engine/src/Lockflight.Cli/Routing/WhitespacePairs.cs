namespace Lockflight.Cli.Routing;

/// <summary>
/// Drops reformatting from a file's diff: a removed line and an added line that are equal once runs of whitespace are collapsed
/// and the ends trimmed are paired and both dropped, so a reindent, a re-wrap of spaces or a CRLF normalization counts as nothing
/// and fires no signal. Pairs keep their order (a longest common subsequence of the collapsed texts): lines that swap places are
/// a change, because the order of statements is behaviour. The text is compared as written (not masked), so editing a string
/// literal or a comment that shares a line with code is still a change.
/// </summary>
internal static class WhitespacePairs
{
    /// <summary>Above this many removed × added lines the pairing walks in order instead (it may pair fewer lines, never more).</summary>
    private const long MaxTableCells = 4_000_000;

    public static (IReadOnlyList<RoutedLine> Added, IReadOnlyList<RoutedLine> Removed) Drop(IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed)
    {
        string[] addedKeys = [.. added.Select(line => Collapse(line.Text))];
        string[] removedKeys = [.. removed.Select(line => Collapse(line.Text))];
        (HashSet<int> pairedAdded, HashSet<int> pairedRemoved) = (long)addedKeys.Length * removedKeys.Length <= MaxTableCells
            ? Common(addedKeys, removedKeys)
            : InOrder(addedKeys, removedKeys);
        return ([.. added.Where((_, index) => !pairedAdded.Contains(index))], [.. removed.Where((_, index) => !pairedRemoved.Contains(index))]);
    }

    /// <summary>The text with every run of whitespace as one space and none at the ends.</summary>
    public static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A longest common subsequence of the two key lists, as the indexes it pairs.</summary>
    private static (HashSet<int> Added, HashSet<int> Removed) Common(string[] added, string[] removed)
    {
        int columns = removed.Length + 1;
        int[] lengths = new int[(added.Length + 1) * columns];
        for (int a = added.Length - 1; a >= 0; a--)
        {
            for (int r = removed.Length - 1; r >= 0; r--)
            {
                lengths[(a * columns) + r] = string.Equals(added[a], removed[r], StringComparison.Ordinal)
                    ? lengths[((a + 1) * columns) + r + 1] + 1
                    : Math.Max(lengths[((a + 1) * columns) + r], lengths[(a * columns) + r + 1]);
            }
        }

        HashSet<int> pairedAdded = [];
        HashSet<int> pairedRemoved = [];
        int addedIndex = 0;
        int removedIndex = 0;
        while (addedIndex < added.Length && removedIndex < removed.Length)
        {
            if (string.Equals(added[addedIndex], removed[removedIndex], StringComparison.Ordinal))
            {
                pairedAdded.Add(addedIndex++);
                pairedRemoved.Add(removedIndex++);
            }
            else if (lengths[((addedIndex + 1) * columns) + removedIndex] >= lengths[(addedIndex * columns) + removedIndex + 1])
            {
                addedIndex++;
            }
            else
            {
                removedIndex++;
            }
        }

        return (pairedAdded, pairedRemoved);
    }

    /// <summary>For very large diffs: each added line pairs with the next equal removed line after the last pair.</summary>
    private static (HashSet<int> Added, HashSet<int> Removed) InOrder(string[] added, string[] removed)
    {
        Dictionary<string, Queue<int>> removedByText = new(StringComparer.Ordinal);
        for (int index = 0; index < removed.Length; index++)
        {
            if (!removedByText.TryGetValue(removed[index], out Queue<int>? queue))
            {
                queue = new Queue<int>();
                removedByText[removed[index]] = queue;
            }

            queue.Enqueue(index);
        }

        HashSet<int> pairedAdded = [];
        HashSet<int> pairedRemoved = [];
        int last = -1;
        for (int index = 0; index < added.Length; index++)
        {
            if (!removedByText.TryGetValue(added[index], out Queue<int>? queue))
            {
                continue;
            }

            while (queue.TryPeek(out int candidate) && candidate <= last)
            {
                queue.Dequeue();
            }

            if (queue.TryDequeue(out int match))
            {
                pairedAdded.Add(index);
                pairedRemoved.Add(match);
                last = match;
            }
        }

        return (pairedAdded, pairedRemoved);
    }
}
