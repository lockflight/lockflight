namespace Lockflight.Cli.Routing;

/// <summary>
/// Reads <c>git diff --raw -z --no-abbrev --no-renames</c>: each record is <c>:oldmode newmode oldid newid status</c> then a NUL and
/// the path then a NUL. Renames and copies are off, so a record has exactly one path.
/// </summary>
internal static class RawDiff
{
    private const int MetaFields = 5;

    public static IReadOnlyList<RawDiffEntry> Parse(string output)
    {
        List<RawDiffEntry> entries = [];
        string[] parts = output.Split('\0');
        for (int index = 0; index + 1 < parts.Length; index += 2)
        {
            string[] meta = parts[index].TrimStart('\n').TrimStart(':').Split(' ');
            if (meta.Length != MetaFields || meta[4].Length == 0)
            {
                continue;
            }

            entries.Add(new RawDiffEntry(meta[0], meta[1], meta[2], meta[3], meta[4][0], parts[index + 1]));
        }

        return entries;
    }
}
