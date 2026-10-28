namespace Lockflight.Cli.Docs;

/// <summary>
/// What every document's checks share during one scan: the working tree's files, the path history (read on first use, since
/// most scans need it for few references) and the parsed documents that links point into.
/// </summary>
internal sealed class DriftContext(RepositoryFiles files, CancellationToken cancellationToken)
{
    private readonly Dictionary<string, IReadOnlySet<string>> anchors = new(StringComparer.Ordinal);

    private Task<PathHistory>? history;

    public RepositoryFiles Files => files;

    public Task<PathHistory> HistoryAsync() => history ??= PathHistory.LoadAsync(files.Root, cancellationToken);

    /// <summary>Whether the history was read, so its notes belong in the report.</summary>
    public bool HistoryRead => history is not null;

    /// <summary>The anchors of a present Markdown file, parsed once per scan.</summary>
    public IReadOnlySet<string> AnchorsOf(string path)
    {
        if (!anchors.TryGetValue(path, out IReadOnlySet<string>? found))
        {
            found = HeadingSlugs.Of(MarkdownDocument.Parse(File.ReadAllText(Path.Combine(files.Root, path))));
            anchors[path] = found;
        }

        return found;
    }
}
