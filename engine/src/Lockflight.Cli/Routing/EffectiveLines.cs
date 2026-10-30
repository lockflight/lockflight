namespace Lockflight.Cli.Routing;

/// <summary>
/// The lines a file really changes. For most files they are its diff lines (whitespace-only pairs already dropped). For the new
/// half of an edited move they are the lines that differ between the old file at base and the new file at head (the old half has
/// none), so a moved file's signals see what its move changed, not every line of both files.
/// </summary>
internal static class EffectiveLines
{
    public static (IReadOnlyList<RoutedLine> Added, IReadOnlyList<RoutedLine> Removed) Of(ChangeAnalysis analysis, RoutedFile file)
    {
        if (file.Move is not FileMove move)
        {
            return (file.Added, file.Removed);
        }

        if (move.Kind == MoveKind.Exact || move.NewPath != file.Path)
        {
            return ([], []);
        }

        RoutedFile old = analysis.Files.First(candidate => candidate.Path == move.OldPath);
        return (Missing(file.Added, old.Removed), Missing(old.Removed, file.Added));
    }

    /// <summary>The lines of <paramref name="lines"/> that <paramref name="other"/> does not hold (whitespace collapsed, as a multiset).</summary>
    private static List<RoutedLine> Missing(IReadOnlyList<RoutedLine> lines, IReadOnlyList<RoutedLine> other)
    {
        Dictionary<string, int> available = new(StringComparer.Ordinal);
        foreach (RoutedLine line in other)
        {
            string key = WhitespacePairs.Collapse(line.Text);
            available[key] = available.GetValueOrDefault(key) + 1;
        }

        List<RoutedLine> missing = [];
        foreach (RoutedLine line in lines)
        {
            string key = WhitespacePairs.Collapse(line.Text);
            if (available.GetValueOrDefault(key) > 0)
            {
                available[key]--;
            }
            else
            {
                missing.Add(line);
            }
        }

        return missing;
    }
}
