using System.Text.RegularExpressions;

namespace Lockflight.Cli.Paths;

/// <summary>
/// Expands a shell wildcard path (<c>*</c>, <c>?</c>, <c>[…]</c>) the way the shell would: against the entries that exist, hidden
/// names only when the pattern segment starts with a dot. A pattern that matches nothing is passed on literally by the shell, so
/// the caller keeps checking it as written too.
/// </summary>
internal static class PathWildcards
{
    public static bool HasWildcard(string path) => GlobOverlap.HasWildcard(path);

    /// <summary>The existing absolute paths the pattern expands to; null when there are more than <paramref name="maxMatches"/>.</summary>
    public static IReadOnlyList<string>? Expand(string path, string workingDirectory, int maxMatches, DirectoryReads reads)
    {
        string[] segments = PathRules.ToAbsolute(path, workingDirectory).Split('/');
        int firstWildcard = Array.FindIndex(segments, GlobOverlap.HasWildcard);
        if (firstWildcard < 0)
        {
            return [];
        }

        string start = string.Join('/', segments[..firstWildcard]);
        List<string> matches = [start.Length == 0 ? "/" : start];
        foreach (string segment in segments[firstWildcard..])
        {
            List<string> next = [];
            Regex? pattern = GlobOverlap.HasWildcard(segment) ? GlobOverlap.SegmentRegex(segment) : null;
            foreach (string directory in matches)
            {
                if (pattern is null)
                {
                    next.Add(Path.Join(directory, segment));
                }
                else if (Directory.Exists(directory))
                {
                    reads.StartListing();
                    foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        reads.SeeEntry();
                        if (Path.GetFileName(entry) is string name && pattern.IsMatch(UnicodeNormalization.Compose(name)) && (!name.StartsWith('.') || segment.StartsWith('.')))
                        {
                            next.Add(entry);
                        }
                    }
                }

                if (next.Count > maxMatches)
                {
                    return null;
                }
            }

            matches = next;
        }

        // Entries listed for a wildcard exist; only a literal segment after the last wildcard needs checking.
        bool literalTail = !GlobOverlap.HasWildcard(segments[^1]);
        return [.. matches.Where(match => !literalTail || Path.Exists(match)).Select(match => match.Replace('\\', '/'))];
    }
}
