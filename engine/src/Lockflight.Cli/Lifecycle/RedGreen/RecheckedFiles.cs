namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// The files of a cycle's open amends, hashed when their re-check starts. The re-check is accepted only while they still have that
/// content, and the cycle's test file hashes then take it, so a file changed during the re-check is not recorded as checked.
/// </summary>
internal sealed class RecheckedFiles(string root, RedGreenCycle open)
{
    private readonly Dictionary<string, string?> hashes = open.OpenAmends.SelectMany(amend => amend.Files).Distinct(StringComparer.Ordinal)
        .ToDictionary(file => file, file => TestFileHashes.Of(root, file), StringComparer.Ordinal);

    /// <summary>The files whose content is not the one the re-check started with.</summary>
    public IReadOnlyList<string> ChangedSince() =>
        [.. hashes.Where(entry => TestFileHashes.Of(root, entry.Key) != entry.Value).Select(entry => entry.Key).Order(StringComparer.Ordinal)];

    /// <summary>The cycle's test file hashes with these files' checked hashes in place (a deleted file dropped); empty stays empty.</summary>
    public IReadOnlyDictionary<string, string> Rehash(IReadOnlyDictionary<string, string> recorded)
    {
        if (recorded.Count == 0)
        {
            return recorded;
        }

        Dictionary<string, string> rehashed = new(recorded, StringComparer.Ordinal);
        foreach ((string file, string? hash) in hashes)
        {
            if (hash is null)
            {
                rehashed.Remove(file);
            }
            else
            {
                rehashed[file] = hash;
            }
        }

        return rehashed;
    }
}
