namespace Lockflight.Cli.Routing;

/// <summary>
/// One side of a change (the merge base, or the head commit or working tree): its commit (null for the working tree), every path
/// it holds, and the text of the files routing reads there — the changed text files, every project file and every
/// <c>*.props</c> / <c>*.targets</c> file. Texts are UTF-8 with the byte order mark dropped and line endings as LF.
/// </summary>
internal sealed record RevisionSnapshot(string? Commit, IReadOnlySet<string> Paths, IReadOnlyDictionary<string, string> Texts)
{
    public static RevisionSnapshot Empty { get; } = new(null, new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));

    public bool Contains(string path) => Paths.Contains(path);

    /// <summary>The text of <paramref name="path"/> on this side; null when the path is not here, is binary or too large to read.</summary>
    public string? TextOf(string path) => Texts.TryGetValue(path, out string? text) ? text : null;
}
