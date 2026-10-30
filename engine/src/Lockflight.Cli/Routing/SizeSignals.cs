using System.Globalization;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Size and shape: how many lines and files of production behaviour the change touches, and whether it adds or deletes production
/// files, changes configuration or binary content. Moves are listed (<c>moved</c>) and raise nothing. All heuristics.
/// </summary>
internal sealed class SizeSignals : IChangeSignalSource
{
    private static readonly FileRole[] CountedRoles = [FileRole.Production, FileRole.Markup, FileRole.ProductionConfig, FileRole.Other];
    private static readonly FileRole[] AddedRoles = [FileRole.Production, FileRole.Markup, FileRole.ProductionConfig, FileRole.Other];
    private static readonly FileRole[] DeletedRoles = [FileRole.Production, FileRole.Markup];
    private static readonly FileRole[] ConfigRoles = [FileRole.ProductionConfig, FileRole.Other];

    public IEnumerable<ChangeSignal> Find(ChangeAnalysis analysis, RoutingPolicy policy)
    {
        RoutingSettings settings = policy.Settings;
        List<RoutedFile> counted = [.. analysis.Files.Where(file => CountedRoles.Contains(file.Role))];
        int size = counted.Sum(file => file.CountedLines);
        int files = counted.Count(file => file.Move is null || (file.Move.Kind == MoveKind.Edited && file.Move.NewPath == file.Path));
        if (size > settings.FixMaxLines)
        {
            yield return Signal(SignalIds.SizeLines, ChangeClass.Behaviour, string.Empty, $"{Count(size)} counted lines, above routing.fix.maxLines {Count(settings.FixMaxLines)}");
        }

        if (files > settings.FixMaxFiles)
        {
            yield return Signal(SignalIds.SizeFiles, ChangeClass.Behaviour, string.Empty, $"{Count(files)} production, configuration or other files, above routing.fix.maxFiles {Count(settings.FixMaxFiles)}");
        }

        if (size > settings.LargeLines)
        {
            yield return Signal(SignalIds.SizeLarge, ChangeClass.Feature, string.Empty, $"{Count(size)} counted lines, above routing.largeLines {Count(settings.LargeLines)}");
        }

        foreach (RoutedFile file in analysis.Files)
        {
            foreach (ChangeSignal signal in FileSignals(file))
            {
                yield return signal;
            }
        }
    }

    private static IEnumerable<ChangeSignal> FileSignals(RoutedFile file)
    {
        // A moved source file is the same code; a moved page, setting or script can change what runs (routes follow page paths,
        // settings are picked up by name and place), so its move counts as a new file.
        bool exactMove = file.Move?.Kind == MoveKind.Exact && file.Role == FileRole.Production;
        if (file.Move is FileMove move && move.NewPath == file.Path)
        {
            yield return Signal(SignalIds.Moved, ChangeClass.Fix, file.Path, move.Kind == MoveKind.Exact
                ? $"moved from {move.OldPath} unchanged"
                : $"moved from {move.OldPath}, {Count(move.DifferingLines)} lines differ");
        }

        if (exactMove)
        {
            yield break;
        }

        if (file.Change.Kind == ChangeKind.Added && AddedRoles.Contains(file.Role))
        {
            yield return Signal(SignalIds.FileAdded, ChangeClass.Behaviour, file.Path, $"new {RoleName(file.Role)} file");
        }

        if (file.Change.Kind == ChangeKind.Deleted && (file.Move is null || file.Role == FileRole.Markup) && DeletedRoles.Contains(file.Role))
        {
            yield return Signal(SignalIds.FileDeleted, ChangeClass.Behaviour, file.Path, "production file deleted");
        }

        if (ConfigRoles.Contains(file.Role) && file.Move?.OldPath != file.Path)
        {
            yield return Signal(SignalIds.ConfigChanged, ChangeClass.Behaviour, file.Path, $"{RoleName(file.Role)} file changed");
        }

        if (file.Change.Binary && CountedRoles.Contains(file.Role))
        {
            yield return Signal(SignalIds.BinaryChanged, ChangeClass.Behaviour, file.Path, "binary content cannot be read");
        }
    }

    private static string RoleName(FileRole role) => role switch
    {
        FileRole.ProductionConfig => "production configuration",
        FileRole.Markup => "markup",
        FileRole.Other => "non-project",
        _ => "production",
    };

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static ChangeSignal Signal(string id, ChangeClass raisesTo, string where, string detail) => new(id, raisesTo, SignalStrength.Heuristic, where, detail);
}
