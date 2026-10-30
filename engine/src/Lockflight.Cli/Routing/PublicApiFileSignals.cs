using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// <c>api.publicApiFile</c> (feature): in a project with a <c>PublicAPI.Shipped.txt</c> at base, a line added to
/// <c>PublicAPI.Unshipped.txt</c> or removed from either file — the public API the project declares changed. Entries moved from
/// Unshipped to Shipped in the same folder (shipping a release) are no change. The files are exact
/// where the analyzer runs in the build, so for such a project <see cref="ApiSignals"/> leaves out its <c>api.publicMember</c>
/// declarations heuristic (<see cref="PublicApiFiles.Enforced"/>); elsewhere both fire. A heuristic over the diff: a pull request
/// edits the files itself, and only the build gate with the analyzer holds the code to them.
/// </summary>
internal sealed class PublicApiFileSignals : IChangeSignalSource
{
    private const string NullableHeader = "#nullable enable";

    public IEnumerable<ChangeSignal> Find(ChangeAnalysis analysis, RoutingPolicy policy)
    {
        List<(RoutedFile File, bool Unshipped, IReadOnlyList<RoutedLine> Added, IReadOnlyList<RoutedLine> Removed)> files = [];
        foreach (RoutedFile file in analysis.Files)
        {
            bool unshipped = PublicApiFiles.IsUnshipped(file.Path);
            if ((unshipped || PublicApiFiles.IsShipped(file.Path)) && (file.AtBase ?? file.AtHead)?.Project is string project
                && PublicApiFiles.HasShippedAtBase(analysis, project))
            {
                (IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed) = EffectiveLines.Of(analysis, file);
                files.Add((file, unshipped, added, removed));
            }
        }

        // Shipping a release moves entries from Unshipped to Shipped in the same folder: those moves are no API change.
        Dictionary<string, int> shippedNow = new(StringComparer.Ordinal);
        foreach ((RoutedFile file, bool unshipped, IReadOnlyList<RoutedLine> added, _) in files.Where(entry => !entry.Unshipped))
        {
            foreach (RoutedLine line in added.Where(IsEntry))
            {
                string key = Key(file.Path, line);
                shippedNow[key] = shippedNow.GetValueOrDefault(key) + 1;
            }
        }

        foreach ((RoutedFile file, bool unshipped, IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed) in files)
        {
            RoutedLine? taken = removed.Where(IsEntry).FirstOrDefault(line => !unshipped || !Shipped(shippedNow, Key(file.Path, line)));
            if (taken is not null)
            {
                yield return Signal($"{file.Move?.OldPath ?? file.Path}:{taken.Line}", $"public API entry removed: {WhitespacePairs.Collapse(taken.Text)}");
            }
            else if (unshipped && added.FirstOrDefault(IsEntry) is RoutedLine entry)
            {
                yield return Signal($"{file.Path}:{entry.Line}", $"public API entry added: {WhitespacePairs.Collapse(entry.Text)}");
            }
        }
    }

    private static ChangeSignal Signal(string where, string detail) =>
        new(SignalIds.ApiPublicApiFile, ChangeClass.Feature, SignalStrength.Heuristic, where, detail);

    private static string Key(string path, RoutedLine line) => $"{ProjectIndex.DirectoryOf(path)}\u0001{WhitespacePairs.Collapse(line.Text)}";

    private static bool Shipped(Dictionary<string, int> shippedNow, string key)
    {
        if (shippedNow.GetValueOrDefault(key) == 0)
        {
            return false;
        }

        shippedNow[key]--;
        return true;
    }

    private static bool IsEntry(RoutedLine line) =>
        line.Text.Trim().Length > 0 && !line.Text.Trim().Equals(NullableHeader, StringComparison.Ordinal);
}
