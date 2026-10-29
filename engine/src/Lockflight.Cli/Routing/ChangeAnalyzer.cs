using Lockflight.Cli.Processes;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Reads a <see cref="ChangeSet"/> for routing, as a pure function of it and the base branch's exclusion globs: each file's role
/// at base and head, its lines with C# masked over the whole file, whitespace-only pairs dropped, moves paired, and the lines it
/// adds to the change's size (docs/guides/risk-routing.md).
/// </summary>
internal static class ChangeAnalyzer
{
    private const string CSharpSuffix = ".cs";

    /// <param name="changeSet">The change as read from git.</param>
    /// <param name="excludedGlobs">The base branch's <c>routing.generatedPaths</c> and <c>routing.nonProduction</c> globs.</param>
    public static ChangeAnalysis Analyze(ChangeSet changeSet, IReadOnlyList<string> excludedGlobs)
    {
        FileRoles baseRoles = new(changeSet.Base, excludedGlobs);
        FileRoles headRoles = new(changeSet.Head, excludedGlobs);
        List<RoutedFile> files = [];
        foreach (ChangedFile change in changeSet.Files)
        {
            FileRoleAt? atBase = change.ExistsAtBase ? baseRoles.Of(change.Path) : null;
            FileRoleAt? atHead = change.ExistsAtHead ? headRoles.Of(change.Path) : null;
            IReadOnlyList<RoutedLine> added = Lines(change.Path, change.Added, change.Binary ? null : changeSet.Head.TextOf(change.Path));
            IReadOnlyList<RoutedLine> removed = Lines(change.Path, change.Removed, change.Binary ? null : changeSet.Base.TextOf(change.Path));
            (added, removed) = WhitespacePairs.Drop(added, removed);
            files.Add(new RoutedFile(change, atBase, atHead, added, removed, Move: null, CountedLines: 0));
        }

        IReadOnlyList<FileMove> moves = MoveDetector.Find(files, changeSet);
        Dictionary<string, FileMove> moveByPath = new(StringComparer.Ordinal);
        foreach (FileMove move in moves)
        {
            moveByPath[move.OldPath] = move;
            moveByPath[move.NewPath] = move;
        }

        List<RoutedFile> routed = [.. files.Select(file => WithMove(file, moveByPath.GetValueOrDefault(file.Path)))];
        return new ChangeAnalysis(changeSet, routed, moves, baseRoles, headRoles);
    }

    public static bool IsCSharp(string path) => path.EndsWith(CSharpSuffix, StringComparison.OrdinalIgnoreCase);

    private static RoutedFile WithMove(RoutedFile file, FileMove? move)
    {
        int counted = file.Change.Binary ? 0 : file.Added.Count(line => line.Counts) + file.Removed.Count(line => line.Counts);
        if (move is not null)
        {
            counted = move.Kind == MoveKind.Edited && move.NewPath == file.Path ? move.DifferingLines : 0;
        }

        return file with { Move = move, CountedLines = counted };
    }

    /// <summary>The diff's lines with their code: for C#, the line of the whole file masked, so a line inside a block comment is no code.</summary>
    private static List<RoutedLine> Lines(string path, IReadOnlyList<DiffLine> lines, string? fileText)
    {
        string[]? masked = fileText is not null && IsCSharp(path) ? CSharpMask.Mask(fileText).Split('\n') : null;
        return [.. lines.Select(line => new RoutedLine(line.Line, line.Text, Code(path, line, masked)))];
    }

    private static string Code(string path, DiffLine line, string[]? masked) =>
        masked is not null && line.Line >= 1 && line.Line <= masked.Length
            ? masked[line.Line - 1]
            : IsCSharp(path) ? CSharpMask.Mask(line.Text) : line.Text;
}
