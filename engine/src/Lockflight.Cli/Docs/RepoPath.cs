namespace Lockflight.Cli.Docs;

/// <summary>Repository-relative paths with <c>/</c> separators, as git prints them; the empty string is the root.</summary>
internal static class RepoPath
{
    public static string Directory(string path) => path.LastIndexOf('/') is int slash and >= 0 ? path[..slash] : string.Empty;

    public static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary><paramref name="relative"/> resolved against <paramref name="directory"/>; null when it climbs above the root.</summary>
    public static string? Combine(string directory, string relative)
    {
        List<string> segments = [.. directory.Split('/', StringSplitOptions.RemoveEmptyEntries)];
        foreach (string segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else if (segment != ".")
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }

    /// <summary>The directory itself, then each ancestor up to the root (the empty string).</summary>
    public static IEnumerable<string> SelfAndAncestors(string directory)
    {
        string current = directory;
        while (true)
        {
            yield return current;
            if (current.Length == 0)
            {
                yield break;
            }

            current = Directory(current);
        }
    }
}
