using Lockflight.Cli.Review;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Pairs deleted and added files into moves. Project files are paired first, so a project that moves with
/// its folder still counts as the same project for the files inside it. A pair needs the same role on its side and the same
/// project; a move across roles or projects is not a move and counts with every line.
/// <list type="bullet">
/// <item>An <b>exact</b> move has the same blob id on both sides; a blob id that occurs on more than one deleted or more than one
/// added path is never paired (not as an edited move either), so the pairing does not depend on file order.</item>
/// <item>An <b>edited</b> move also needs the same extension and a Dice similarity of at least 0.80 over the multisets of the two
/// files' code lines (whitespace collapsed, comment-only and blank lines left out); pairs are chosen greedily, highest
/// similarity first, ties by ordinal old path then new path. Above 200 × 200 candidates no edited move is looked for.</item>
/// </list>
/// </summary>
internal static class MoveDetector
{
    public const double MinimumSimilarity = 0.80;
    public const int MaxCandidatesPerSide = 200;

    public static IReadOnlyList<FileMove> Find(IReadOnlyList<RoutedFile> files, ChangeSet changeSet)
    {
        List<RoutedFile> deleted = [.. files.Where(file => file.Change.Kind == ChangeKind.Deleted)];
        List<RoutedFile> added = [.. files.Where(file => file.Change.Kind == ChangeKind.Added)];
        List<FileMove> moves = [];
        Dictionary<string, string> projectMoves = new(StringComparer.Ordinal);

        Pair(Projects(deleted), Projects(added), changeSet, moves, (_, _) => true);
        foreach (FileMove move in moves)
        {
            projectMoves[move.OldPath] = move.NewPath;
        }

        Pair(Others(deleted, moves), Others(added, moves), changeSet, moves, (from, to) => SameProject(from.AtBase!.Project, to.AtHead!.Project, projectMoves));
        return [.. moves.OrderBy(move => move.NewPath, StringComparer.Ordinal)];
    }

    /// <summary>The code lines of a text, as the similarity compares them.</summary>
    public static Dictionary<string, int> CodeLines(string path, string text)
    {
        string[] raw = text.Split('\n');
        string[] code = ChangeAnalyzer.IsCSharp(path) ? CSharpMask.Mask(text).Split('\n') : raw;
        Dictionary<string, int> lines = new(StringComparer.Ordinal);
        for (int index = 0; index < raw.Length; index++)
        {
            if (code[index].AsSpan().Trim().Length == 0)
            {
                continue;
            }

            string key = WhitespacePairs.Collapse(raw[index]);
            lines[key] = lines.GetValueOrDefault(key) + 1;
        }

        return lines;
    }

    private static void Pair(
        List<RoutedFile> deleted, List<RoutedFile> added, ChangeSet changeSet, List<FileMove> moves, Func<RoutedFile, RoutedFile, bool> sameProject)
    {
        HashSet<string> ambiguous = PairExact(deleted, added, moves, sameProject);
        HashSet<string> paired = new(moves.SelectMany(move => new[] { move.OldPath, move.NewPath }), StringComparer.Ordinal);
        List<RoutedFile> openDeleted = [.. deleted.Where(file => !paired.Contains(file.Path) && !ambiguous.Contains(file.Path))];
        List<RoutedFile> openAdded = [.. added.Where(file => !paired.Contains(file.Path) && !ambiguous.Contains(file.Path))];
        if (openDeleted.Count > MaxCandidatesPerSide && openAdded.Count > MaxCandidatesPerSide)
        {
            return;
        }

        PairEdited(openDeleted, openAdded, changeSet, moves, sameProject);
    }

    /// <summary>Pairs files with the same unique blob id; returns the paths whose blob id is shared, which are never paired at all.</summary>
    private static HashSet<string> PairExact(List<RoutedFile> deleted, List<RoutedFile> added, List<FileMove> moves, Func<RoutedFile, RoutedFile, bool> sameProject)
    {
        Dictionary<string, List<RoutedFile>> deletedByBlob = ByBlob(deleted, file => file.Change.OldBlob);
        Dictionary<string, List<RoutedFile>> addedByBlob = ByBlob(added, file => file.Change.NewBlob);
        foreach ((string blob, List<RoutedFile> from) in deletedByBlob.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (from.Count == 1 && addedByBlob.TryGetValue(blob, out List<RoutedFile>? to) && to.Count == 1
                && from[0].AtBase!.Role == to[0].AtHead!.Role && sameProject(from[0], to[0]))
            {
                moves.Add(new FileMove(from[0].Path, to[0].Path, MoveKind.Exact, DifferingLines: 0, Similarity: 1));
            }
        }

        return new HashSet<string>(
            deletedByBlob.Values.Concat(addedByBlob.Values).Where(group => group.Count > 1).SelectMany(group => group).Select(file => file.Path),
            StringComparer.Ordinal);
    }

    private static void PairEdited(List<RoutedFile> deleted, List<RoutedFile> added, ChangeSet changeSet, List<FileMove> moves, Func<RoutedFile, RoutedFile, bool> sameProject)
    {
        List<(double Similarity, int Differing, RoutedFile From, RoutedFile To)> candidates = [];
        Dictionary<string, Dictionary<string, int>> addedLines = new(StringComparer.Ordinal);
        foreach (RoutedFile to in added)
        {
            if (changeSet.Head.TextOf(to.Path) is string text)
            {
                addedLines[to.Path] = CodeLines(to.Path, text);
            }
        }

        foreach (RoutedFile from in deleted)
        {
            if (changeSet.Base.TextOf(from.Path) is not string fromText)
            {
                continue;
            }

            Dictionary<string, int> fromLines = CodeLines(from.Path, fromText);
            foreach (RoutedFile to in added)
            {
                if (addedLines.TryGetValue(to.Path, out Dictionary<string, int>? toLines)
                    && from.AtBase!.Role == to.AtHead!.Role
                    && string.Equals(Path.GetExtension(from.Path), Path.GetExtension(to.Path), StringComparison.OrdinalIgnoreCase)
                    && sameProject(from, to)
                    && Similarity(fromLines, toLines) is (double similarity, int differing)
                    && similarity >= MinimumSimilarity)
                {
                    candidates.Add((similarity, differing, from, to));
                }
            }
        }

        HashSet<string> taken = new(StringComparer.Ordinal);
        foreach ((double similarity, int differing, RoutedFile from, RoutedFile to) in candidates
            .OrderByDescending(candidate => candidate.Similarity)
            .ThenBy(candidate => candidate.From.Path, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.To.Path, StringComparer.Ordinal))
        {
            if (taken.Add(from.Path) && taken.Add(to.Path))
            {
                moves.Add(new FileMove(from.Path, to.Path, MoveKind.Edited, differing, similarity));
            }
            else
            {
                taken.Remove(from.Path);
            }
        }
    }

    /// <summary>Dice over line multisets, 2·|a∩b| / (|a| + |b|), and the lines that differ, |a| + |b| − 2·|a∩b|.</summary>
    private static (double Similarity, int Differing) Similarity(Dictionary<string, int> first, Dictionary<string, int> second)
    {
        int firstCount = first.Values.Sum();
        int secondCount = second.Values.Sum();
        if (firstCount + secondCount == 0)
        {
            return (0, 0);
        }

        int common = first.Sum(entry => Math.Min(entry.Value, second.GetValueOrDefault(entry.Key)));
        return (2.0 * common / (firstCount + secondCount), firstCount + secondCount - (2 * common));
    }

    private static bool SameProject(string? from, string? to, Dictionary<string, string> projectMoves) =>
        string.Equals(from, to, StringComparison.Ordinal) || (from is not null && projectMoves.TryGetValue(from, out string? moved) && moved == to);

    private static List<RoutedFile> Projects(List<RoutedFile> files) => [.. files.Where(file => ProjectIndex.IsProjectFile(file.Path))];

    private static List<RoutedFile> Others(List<RoutedFile> files, List<FileMove> moves) =>
        [.. files.Where(file => !ProjectIndex.IsProjectFile(file.Path) && !moves.Any(move => move.OldPath == file.Path || move.NewPath == file.Path))];

    private static Dictionary<string, List<RoutedFile>> ByBlob(List<RoutedFile> files, Func<RoutedFile, string?> blob)
    {
        Dictionary<string, List<RoutedFile>> byBlob = new(StringComparer.Ordinal);
        foreach (RoutedFile file in files)
        {
            if (blob(file) is string id)
            {
                if (!byBlob.TryGetValue(id, out List<RoutedFile>? group))
                {
                    group = [];
                    byBlob[id] = group;
                }

                group.Add(file);
            }
        }

        return byBlob;
    }
}
