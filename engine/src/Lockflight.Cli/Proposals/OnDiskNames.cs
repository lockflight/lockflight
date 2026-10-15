namespace Lockflight.Cli.Proposals;

/// <summary>
/// A project-relative path spelled as the file system stores it. On a file system that ignores case (macOS, Windows),
/// <c>SRC/app.cs</c> opens the same file as <c>src/App.cs</c>, so the patch, the protected-path rule and the check for a file
/// named twice all use the stored name, which is the one git knows. A segment that does not exist yet is kept as typed. Unicode
/// normalization is not compared: the engine runs without culture data, so a decomposed spelling of a composed name is kept as
/// typed (the patch still names the path that is shown, and the file system opens it).
/// </summary>
internal static class OnDiskNames
{
    public static string Spell(string root, string relativePath)
    {
        string[] segments = relativePath.Split('/');
        string directory = root;
        for (int index = 0; index < segments.Length && Directory.Exists(directory); index++)
        {
            if (StoredName(directory, segments[index]) is not string stored)
            {
                break;
            }

            segments[index] = stored;
            directory = Path.Combine(directory, stored);
        }

        return string.Join('/', segments);
    }

    /// <summary>Whether two names would open the same file on a file system that ignores case.</summary>
    public static bool SameName(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    /// <summary>The entry of <paramref name="directory"/> that <paramref name="name"/> opens; null when it opens none.</summary>
    private static string? StoredName(string directory, string name)
    {
        string? lookAlike = null;
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            string entryName = Path.GetFileName(entry);
            if (entryName == name)
            {
                return name;
            }

            lookAlike ??= SameName(entryName, name) ? entryName : null;
        }

        // A look-alike counts only when the typed name really opens it; on a case-sensitive file system it is another file.
        return lookAlike is not null && Path.Exists(Path.Combine(directory, name)) ? lookAlike : null;
    }
}
