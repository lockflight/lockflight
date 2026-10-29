using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Paths;

/// <summary>
/// Whether a manifest glob could match a path that holds shell wildcards itself. Over-approximates: when unsure, it overlaps.
/// </summary>
internal static class GlobOverlap
{
    private const string AnyDepth = "**";

    private static readonly ConcurrentDictionary<string, Regex> SegmentRegexes = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether a '/'-separated path relative to the root, possibly holding wildcards and <c>**</c>, could match the glob; for a tree,
    /// also any path below it.
    /// </summary>
    public static bool CouldMatch(string glob, string relativePath, bool asTree) => Matches(Segments(glob), 0, Segments(relativePath), 0, asTree);

    /// <summary>Whether a path segment contains shell wildcards (<c>*</c>, <c>?</c>, <c>[…]</c>).</summary>
    public static bool HasWildcard(string segment) => segment.IndexOfAny(['*', '?', '[']) >= 0;

    /// <summary>A single-segment shell pattern as a regex (case-insensitive, like the protected-path matcher).</summary>
    public static Regex SegmentRegex(string pattern) => SegmentRegexes.GetOrAdd(UnicodeNormalization.Compose(pattern), CompileSegment);

    private static Regex CompileSegment(string pattern)
    {
        StringBuilder regex = new("^");
        for (int index = 0; index < pattern.Length; index++)
        {
            char character = pattern[index];
            int classEnd = character == '[' ? pattern.IndexOf(']', index + 2) : -1;
            if (character == '*')
            {
                regex.Append("[^/]*");
            }
            else if (character == '?')
            {
                regex.Append("[^/]");
            }
            else if (classEnd > index)
            {
                string body = pattern[(index + 1)..classEnd];
                body = body.StartsWith('!') ? "^" + body[1..] : body;
                regex.Append('[').Append(body.Replace("\\", "\\\\", StringComparison.Ordinal)).Append(']');
                index = classEnd;
            }
            else
            {
                regex.Append(Regex.Escape(character.ToString()));
            }
        }

        try
        {
            return new Regex(regex.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            // A class the regex engine refuses ([z-a]) is taken to match any name: when unsure, it overlaps.
            return new Regex("^[^/]*$", RegexOptions.CultureInvariant);
        }
    }

    private static bool Matches(string[] glob, int globIndex, string[] path, int pathIndex, bool asTree)
    {
        if (pathIndex == path.Length)
        {
            // A tree holds whatever the rest of the glob asks for; a file must end where the glob does.
            return asTree || glob.Skip(globIndex).All(segment => segment == AnyDepth);
        }

        if (globIndex == glob.Length)
        {
            return false;
        }

        if (glob[globIndex] == AnyDepth)
        {
            return Matches(glob, globIndex + 1, path, pathIndex, asTree) || Matches(glob, globIndex, path, pathIndex + 1, asTree);
        }

        if (path[pathIndex] == AnyDepth)
        {
            return Matches(glob, globIndex, path, pathIndex + 1, asTree) || Matches(glob, globIndex + 1, path, pathIndex, asTree);
        }

        return SegmentsOverlap(glob[globIndex], path[pathIndex]) && Matches(glob, globIndex + 1, path, pathIndex + 1, asTree);
    }

    private static string[] Segments(string path) => UnicodeNormalization.Compose(path).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool SegmentsOverlap(string globSegment, string pathSegment)
    {
        bool globWild = HasWildcard(globSegment);
        bool pathWild = HasWildcard(pathSegment) || pathSegment == AnyDepth;
        if (!globWild && !pathWild)
        {
            return string.Equals(globSegment, pathSegment, StringComparison.OrdinalIgnoreCase);
        }

        if (!pathWild)
        {
            return SegmentRegex(globSegment).IsMatch(pathSegment);
        }

        if (!globWild)
        {
            return SegmentRegex(pathSegment).IsMatch(globSegment);
        }

        // Two patterns can only both match a name when their literal heads and tails agree.
        return Compatible(LiteralHead(globSegment), LiteralHead(pathSegment), head: true)
            && Compatible(LiteralTail(globSegment), LiteralTail(pathSegment), head: false);
    }

    private static string LiteralHead(string pattern) => pattern[..pattern.IndexOfAny(['*', '?', '['])];

    private static string LiteralTail(string pattern) => pattern[(pattern.LastIndexOfAny(['*', '?', ']']) + 1)..];

    private static bool Compatible(string first, string second, bool head) => head
        ? first.StartsWith(second, StringComparison.OrdinalIgnoreCase) || second.StartsWith(first, StringComparison.OrdinalIgnoreCase)
        : first.EndsWith(second, StringComparison.OrdinalIgnoreCase) || second.EndsWith(first, StringComparison.OrdinalIgnoreCase);
}
