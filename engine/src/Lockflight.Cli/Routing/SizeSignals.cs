using System.Globalization;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Size and shape: how many lines and files of production behaviour the change touches, and whether it adds or deletes production
/// files, changes configuration or binary content. Moves are listed (<c>moved</c>) and raise nothing. A production or markup file
/// whose text cannot be read on a side where it exists (binary to git, a NUL byte, above <see cref="RevisionText.MaxBytes"/>) hides
/// every content signal (types, endpoints, the database model), so it raises to <c>feature</c> on its own
/// (<c>content.unreadable</c>). All heuristics.
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

            if (Unreadable(analysis.ChangeSet, file) is string side)
            {
                yield return Signal(SignalIds.ContentUnreadable, ChangeClass.Feature, file.Path, $"the {side} text cannot be read (binary, a NUL byte or above {Count(RevisionText.MaxBytes / (1024 * 1024))} MB), so no content signal can see it");
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

    /// <summary>Which side of a production or markup file at head has no readable text, or null when every side it exists on does.</summary>
    private static string? Unreadable(ChangeSet changeSet, RoutedFile file)
    {
        if (file.Role is not (FileRole.Production or FileRole.Markup))
        {
            return null;
        }

        bool head = file.Change.ExistsAtHead && (file.Change.Binary || changeSet.Head.TextOf(file.Path) is null);
        // A deleted file is file.deleted already: its base side alone raises nothing more.
        bool atBase = file.Change.ExistsAtHead && file.Change.ExistsAtBase && (file.Change.Binary || changeSet.Base.TextOf(file.Path) is null);
        return (head, atBase) switch
        {
            (true, true) => "base and head",
            (true, false) => "head",
            (false, true) => "base",
            _ => null,
        };
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
