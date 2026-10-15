namespace Lockflight.Cli.Paths;

/// <summary>
/// Resolves symbolic links component by component (the OS has no portable realpath in .NET), so an alias such as a symlinked
/// directory or macOS <c>/var</c> → <c>/private/var</c> cannot route a write around a protected-path rule. Components that do
/// not exist yet are kept as written. Hard links share no path and are out of reach here (backstopped by CI).
/// </summary>
internal static class RealPath
{
    private const int MaxLinkHops = 40;

    public static string Resolve(string absolutePath)
    {
        string pending = absolutePath.Replace('\\', '/');
        for (int hop = 0; hop < MaxLinkHops; hop++)
        {
            (string resolved, bool restarted) = ResolveOnce(pending);
            if (!restarted)
            {
                return resolved;
            }

            pending = resolved;
        }

        throw new IOException($"Too many levels of symbolic links resolving '{absolutePath}'.");
    }

    /// <summary>
    /// Walks the path until the first link; a link's target replaces the prefix and the walk restarts, because the target itself
    /// may contain links (e.g. a link written as /var/... on macOS).
    /// </summary>
    private static (string Path, bool Restarted) ResolveOnce(string path)
    {
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string current = path.StartsWith('/') ? "/" : string.Empty;

        for (int index = 0; index < segments.Length; index++)
        {
            if (current.Length == 0 && segments[index].EndsWith(':'))
            {
                // A drive letter ("C:") roots the walk on Windows.
                current = segments[index] + "/";
                continue;
            }

            string candidate = Path.Join(current, segments[index]);
            FileSystemInfo entry = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
            if (!entry.Exists)
            {
                return (Path.Join(current, string.Join('/', segments[index..])), false);
            }

            if (entry.LinkTarget is string linkTarget)
            {
                string target = Path.IsPathRooted(linkTarget) ? linkTarget : Path.Join(current, linkTarget);
                string rest = string.Join('/', segments[(index + 1)..]);
                return (rest.Length == 0 ? target : Path.Join(target, rest), true);
            }

            current = candidate;
        }

        return (current, false);
    }
}
