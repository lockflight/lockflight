namespace Lockflight.Cli.Hooks;

/// <summary>What one simple command writes, with paths as written (see <see cref="ShellWrites"/>).</summary>
internal sealed record CommandWrites(IReadOnlyList<string> Files, IReadOnlyList<string> Trees, IReadOnlyList<string> SourceTrees)
{
    public static CommandWrites None { get; } = new([], [], []);

    public static CommandWrites OfFiles(IReadOnlyList<string> files) => new(files, [], []);

    /// <summary>Each path counts as a file and as a directory whose contents may be rewritten.</summary>
    public static CommandWrites OfFilesAndTrees(IReadOnlyList<string> paths) => new(paths, paths, []);
}
