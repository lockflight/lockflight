namespace Lockflight.Cli.Review;

/// <summary>
/// The part of the code change the reviewer is shown: the whole diff when it fits the limit, else the files whose diffs fit whole
/// (the first file cut at the limit when none does), followed by a note that names the files not shown in full so the reviewer can
/// read them. <see cref="Whole"/> is the whole diff, <see cref="Files"/> the number of changed files, and
/// <see cref="NotShown"/> the paths (with a unit's path in a workspace) of the files whose diff the reviewer did not see whole.
/// </summary>
internal sealed record ReviewDiff(string Whole, string Shown, int Limit, int Files, IReadOnlyList<string> NotShown)
{
    private const string FileHeader = "diff --git ";

    /// <summary>The whole diff's length in characters.</summary>
    public int Length => Whole.Length;

    /// <summary>Whether the reviewer is shown the whole diff.</summary>
    public bool IsComplete => NotShown.Count == 0;

    public static ReviewDiff Fit(string diff, int maxCharacters)
    {
        string[] sections = Sections(diff);
        if (diff.Length <= maxCharacters)
        {
            return new ReviewDiff(diff, diff, maxCharacters, sections.Length, []);
        }

        List<string> shown = [];
        List<string> notShown = [];
        int length = 0;
        foreach (string section in sections)
        {
            if (length + section.Length <= maxCharacters)
            {
                shown.Add(section);
                length += section.Length;
            }
            else
            {
                notShown.Add(PathOf(section));
            }
        }

        string text = shown.Count > 0 ? string.Concat(shown) : diff[..maxCharacters] + "\n";
        string note = $"[diff truncated: {notShown.Count} of {sections.Length} changed files not shown in full; read them: {string.Join(", ", notShown)}]";
        return new ReviewDiff(diff, text + note, maxCharacters, sections.Length, notShown);
    }

    /// <summary>The diff split before each file header; text before the first header, if any, stays with the first section.</summary>
    private static string[] Sections(string diff)
    {
        List<string> sections = [];
        int start = 0;
        int next = diff.IndexOf("\n" + FileHeader, StringComparison.Ordinal);
        while (next >= 0)
        {
            sections.Add(diff[start..(next + 1)]);
            start = next + 1;
            next = diff.IndexOf("\n" + FileHeader, start, StringComparison.Ordinal);
        }

        if (start < diff.Length)
        {
            sections.Add(diff[start..]);
        }

        return [.. sections];
    }

    /// <summary>The new-side path of a file's diff, from <c>diff --git a/… b/…</c> (quoted when git quotes it); the header line otherwise.</summary>
    private static string PathOf(string section)
    {
        int lineEnd = section.IndexOf('\n', StringComparison.Ordinal);
        string header = lineEnd < 0 ? section : section[..lineEnd];
        int quoted = header.LastIndexOf(" \"b/", StringComparison.Ordinal);
        if (quoted >= 0 && header.EndsWith('"'))
        {
            return header[(quoted + 4)..^1];
        }

        int plain = header.LastIndexOf(" b/", StringComparison.Ordinal);
        return plain >= 0 ? header[(plain + 3)..] : header;
    }
}
