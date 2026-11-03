namespace Lockflight.Cli.Paths;

/// <summary>
/// Whether one manifest glob matches every path another one matches, under <see cref="PathRules"/>' semantics (case-insensitive,
/// <c>*</c> and <c>?</c> within a segment, <c>**/</c> any depth, a trailing <c>**</c> anything below). Under-approximates: two
/// patterns' literal parts compare by case, and when unsure (a bracket, a <c>**</c> inside a segment, an empty, <c>.</c> or <c>..</c> segment), only the same text covers a glob.
/// </summary>
internal static class GlobContainment
{
    private const string AnyDepth = "**";

    /// <summary>Whether every path <paramref name="inner"/> matches is also matched by <paramref name="outer"/>.</summary>
    public static bool Covers(string outer, string inner)
    {
        if (string.Equals(outer, inner, StringComparison.Ordinal))
        {
            return true;
        }

        return Segments(outer) is string[] outerSegments && Segments(inner) is string[] innerSegments
            && Contains(outerSegments, 0, innerSegments, 0);
    }

    private static bool Contains(string[] outer, int outerIndex, string[] inner, int innerIndex)
    {
        if (innerIndex == inner.Length)
        {
            return outerIndex == outer.Length;
        }

        if (outerIndex == outer.Length)
        {
            return false;
        }

        if (outer[outerIndex] == AnyDepth)
        {
            // A trailing ** takes whatever is left (never nothing, and the inner glob always leaves something); a ** before a
            // segment takes no segment or one more of the inner glob's, a ** of its own included.
            return outerIndex == outer.Length - 1
                || Contains(outer, outerIndex + 1, inner, innerIndex)
                || Contains(outer, outerIndex, inner, innerIndex + 1);
        }

        // One outer segment cannot hold the any-depth run of the inner glob.
        return inner[innerIndex] != AnyDepth
            && SegmentCovers(outer[outerIndex], inner[innerIndex])
            && Contains(outer, outerIndex + 1, inner, innerIndex + 1);
    }

    private static bool SegmentCovers(string outer, string inner)
    {
        if (string.Equals(outer, inner, StringComparison.Ordinal) || outer == "*")
        {
            return true;
        }

        if (!GlobOverlap.HasWildcard(inner))
        {
            return GlobOverlap.SegmentRegex(outer).IsMatch(inner);
        }

        // Both wild: only "head*tail" against a pattern whose literal head and tail already hold them is sure.
        int star = outer.IndexOf('*', StringComparison.Ordinal);
        if (star < 0 || outer.IndexOfAny(['*', '?'], star + 1) >= 0 || outer.Contains('?', StringComparison.Ordinal))
        {
            return false;
        }

        string innerHead = inner[..inner.IndexOfAny(['*', '?'])];
        string innerTail = inner[(inner.LastIndexOfAny(['*', '?']) + 1)..];
        return innerHead.StartsWith(outer[..star], StringComparison.Ordinal)
            && innerTail.EndsWith(outer[(star + 1)..], StringComparison.Ordinal);
    }

    /// <summary>The glob's segments, or null when it holds a form this comparison does not model.</summary>
    private static string[]? Segments(string glob)
    {
        string[] segments = UnicodeNormalization.Compose(glob).Replace('\\', '/').Split('/');
        bool modelled = segments.All(segment => segment.Length > 0 && segment is not ("." or "..") && !segment.Contains('[', StringComparison.Ordinal)
            && (segment == AnyDepth || !segment.Contains(AnyDepth, StringComparison.Ordinal)));
        return modelled ? segments : null;
    }
}
