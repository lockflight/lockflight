using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Hooks.Guard;

/// <summary>
/// A shell wildcard target split where the guard can judge it: the folder before its first wildcard, which is a literal path, and
/// the rest; and whether that rest can reach into a folder below the prefix (<c>~/.lockflig?/bin/lockflight</c> reaches into
/// <c>~/.lockflight</c>, <c>../p*/lockflight.yaml</c> into the project <c>../project</c>).
/// </summary>
internal static class WildcardPatterns
{
    private const string AnyDepth = "**";

    /// <summary>The folder before the first wildcard (the working directory when the pattern starts with one, <c>/</c> for the filesystem root) and the segments from it on.</summary>
    public static (string Prefix, string[] Remainder) Split(string pattern)
    {
        string[] segments = pattern.Replace('\\', '/').Split('/');
        int firstWildcard = Array.FindIndex(segments, GlobOverlap.HasWildcard);
        if (firstWildcard < 0)
        {
            return (pattern, []);
        }

        string prefix = firstWildcard == 0 ? ShellArguments.CurrentDirectory : string.Join('/', segments[..firstWildcard]);
        return (prefix.Length == 0 ? "/" : prefix, segments[firstWildcard..]);
    }

    /// <summary>
    /// The part of <paramref name="rest"/> below <paramref name="below"/> (a '/'-separated folder relative to the prefix), when the
    /// pattern can reach into it: empty when the pattern ends at that folder or above it (it names the folder itself), the
    /// remaining segments otherwise; null when no segment can match. A <c>**</c> reaches any depth, so it and what follows are kept.
    /// </summary>
    public static string? Into(string[] rest, string below)
    {
        string[] folder = below.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < folder.Length; index++)
        {
            if (index == rest.Length)
            {
                return string.Empty;
            }

            if (rest[index] == AnyDepth)
            {
                return string.Join('/', rest[index..]);
            }

            if (!GlobOverlap.CouldMatch(folder[index], rest[index], asTree: false))
            {
                return null;
            }
        }

        return string.Join('/', rest[folder.Length..]);
    }
}
