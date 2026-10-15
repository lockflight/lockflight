namespace Lockflight.Cli.Init;

/// <summary>Appends Lockflight's local-state entries to <c>.gitignore</c> under one marked block, once.</summary>
internal static class GitIgnoreUpdater
{
    private const string BlockHeader = "# Lockflight local state";

    public static (string Content, bool Changed) Update(string? existing) => Update(existing, []);

    /// <param name="childRepositories">A workspace shell's child repositories (<c>services/orders/</c>), ignored by the shell's git.</param>
    public static (string Content, bool Changed) Update(string? existing, IReadOnlyList<string> childRepositories)
    {
        string content = existing ?? string.Empty;
        HashSet<string> present = [.. content.Split('\n').Select(line => line.Trim())];
        List<string> missing = [.. Templates.GitIgnoreEntries.Concat(childRepositories).Where(entry => !present.Contains(entry) && !present.Contains("/" + entry) && !CoveredByParent(present, entry))];
        if (missing.Count == 0)
        {
            return (content, false);
        }

        string separator = content.Length == 0 || content.EndsWith('\n') ? string.Empty : "\n";
        string block = (content.Length == 0 ? string.Empty : "\n") + BlockHeader + "\n" + string.Join('\n', missing) + "\n";
        return (content + separator + block, true);
    }

    /// <summary>Whether a line already ignores a parent folder of the entry (<c>services/</c> covers <c>services/orders/</c>).</summary>
    private static bool CoveredByParent(HashSet<string> present, string entry)
    {
        string[] segments = entry.TrimEnd('/').Split('/');
        return Enumerable.Range(1, segments.Length - 1).Select(count => string.Join('/', segments.Take(count)) + "/").Any(parent => present.Contains(parent) || present.Contains("/" + parent));
    }
}
