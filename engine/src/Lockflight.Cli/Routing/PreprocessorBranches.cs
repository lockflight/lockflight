namespace Lockflight.Cli.Routing;

/// <summary>
/// One reading of C# source through its conditional compilation: every <c>#if</c> group keeps its first branch (or its last) and
/// the other branches' lines are blanked, line breaks kept. A header split across <c>#if</c>/<c>#else</c> then has balanced braces,
/// and reading the source once with each choice sees the declarations of every branch.
/// </summary>
internal static class PreprocessorBranches
{
    public static string Keep(string source, bool firstBranch)
    {
        string[] lines = source.Split('\n');
        Stack<(int Branch, int Count)> groups = new();
        List<int> branchCounts = CountBranches(lines);
        int group = 0;
        for (int index = 0; index < lines.Length; index++)
        {
            string directive = lines[index].TrimStart();
            if (directive.StartsWith("#if", StringComparison.Ordinal))
            {
                groups.Push((0, branchCounts[group++]));
                continue;
            }

            if (groups.Count > 0 && (directive.StartsWith("#elif", StringComparison.Ordinal) || directive.StartsWith("#else", StringComparison.Ordinal)))
            {
                (int branch, int count) = groups.Pop();
                groups.Push((branch + 1, count));
                continue;
            }

            if (groups.Count > 0 && directive.StartsWith("#endif", StringComparison.Ordinal))
            {
                groups.Pop();
                continue;
            }

            if (groups.Any(open => open.Branch != (firstBranch ? 0 : open.Count - 1)))
            {
                lines[index] = new string(' ', lines[index].Length);
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>How many branches each <c>#if</c> group has, in the order the groups open.</summary>
    private static List<int> CountBranches(string[] lines)
    {
        List<int> counts = [];
        Stack<int> open = new();
        foreach (string line in lines)
        {
            string directive = line.TrimStart();
            if (directive.StartsWith("#if", StringComparison.Ordinal))
            {
                open.Push(counts.Count);
                counts.Add(1);
            }
            else if (open.Count > 0 && (directive.StartsWith("#elif", StringComparison.Ordinal) || directive.StartsWith("#else", StringComparison.Ordinal)))
            {
                counts[open.Peek()]++;
            }
            else if (open.Count > 0 && directive.StartsWith("#endif", StringComparison.Ordinal))
            {
                open.Pop();
            }
        }

        return counts;
    }
}
