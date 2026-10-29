namespace Lockflight.Cli.Routing;

/// <summary>
/// A <see cref="ChangeSet"/> read for routing (<see cref="ChangeAnalyzer"/>): every changed file with its role, lines and move,
/// sorted by path, the moves found, and the role readers of both sides for signals that ask about other paths.
/// </summary>
internal sealed record ChangeAnalysis(ChangeSet ChangeSet, IReadOnlyList<RoutedFile> Files, IReadOnlyList<FileMove> Moves, FileRoles BaseRoles, FileRoles HeadRoles);
