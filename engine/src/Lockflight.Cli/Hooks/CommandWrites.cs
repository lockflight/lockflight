namespace Lockflight.Cli.Hooks;

/// <summary>
/// What one simple command writes, with paths as written (relative to the command's own directory). <see cref="Hidden"/> names a
/// writer whose targets cannot be seen in the command line (<c>xargs rm</c>). <see cref="PatternFiles"/> are paths of the form
/// <c>DIR/**/NAME-PATTERN</c>: files matching a name anywhere below a directory (<c>find DIR -name '*.tmp' -delete</c>).
/// </summary>
internal sealed record CommandWrites(IReadOnlyList<string> Files, IReadOnlyList<string> Trees, IReadOnlyList<string> SourceTrees, string? Hidden)
{
    public static CommandWrites None { get; } = new([], [], [], null);

    public IReadOnlyList<string> PatternFiles { get; init; } = [];

    public static CommandWrites OfFiles(IReadOnlyList<string> files) => new(files, [], [], null);

    public static CommandWrites OfTrees(IReadOnlyList<string> trees) => new([], trees, [], null);

    /// <summary>Each path counts as a file and as a directory whose contents may be rewritten.</summary>
    public static CommandWrites OfFilesAndTrees(IReadOnlyList<string> paths) => new(paths, paths, [], null);

    /// <summary>A writer whose targets the command line does not show; it is blocked.</summary>
    public static CommandWrites Hiding(string description) => new([], [], [], description);
}
