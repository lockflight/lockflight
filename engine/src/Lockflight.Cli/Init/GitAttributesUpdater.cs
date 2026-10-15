namespace Lockflight.Cli.Init;

/// <summary>
/// Adds the standard profile's <c>.gitattributes</c> rule: Lockflight's feature records are checked out byte-exact
/// (<c>-text</c>), so an approval signed on one OS verifies on another despite <c>core.autocrlf</c>.
/// </summary>
internal static class GitAttributesUpdater
{
    public const string Rule = ".lockflight/features/** -text";

    private const string BlockHeader = "# Lockflight evidence: byte-exact for signature checks";

    public static (string Content, bool Changed) Update(string? existing)
    {
        string content = existing ?? string.Empty;
        if (content.Split('\n').Any(line => line.Trim() == Rule))
        {
            return (content, false);
        }

        string separator = content.Length == 0 || content.EndsWith('\n') ? string.Empty : "\n";
        return (content + separator + (content.Length == 0 ? string.Empty : "\n") + BlockHeader + "\n" + Rule + "\n", true);
    }
}
