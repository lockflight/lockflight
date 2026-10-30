namespace Lockflight.Cli.Adoption;

/// <summary>Whether a path under a root goes through a symbolic link, so adoption never writes through one to outside the project.</summary>
internal static class SymbolicLinks
{
    /// <summary>The first symbolic link on <paramref name="relative"/> ('/'-separated, under <paramref name="root"/>) — a folder on the way or the file itself — or null.</summary>
    public static string? OnPath(string root, string relative)
    {
        string current = root;
        string[] segments = relative.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            FileSystemInfo entry = index < segments.Length - 1 ? new DirectoryInfo(current) : new FileInfo(current);
            // LinkTarget is set for a dangling link too, which Exists does not report.
            if (entry.LinkTarget is not null)
            {
                return string.Join('/', segments.Take(index + 1));
            }

            if (!entry.Exists)
            {
                return null;
            }
        }

        return null;
    }
}
