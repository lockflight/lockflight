namespace Lockflight.Cli.Routing;

/// <summary>
/// A changed file with what routing derived from it: its role and owning project at base and at head (null on a side where it
/// does not exist), the role and project it is judged by (the base side's when it exists there), its added and removed lines
/// after whitespace-only pairs were dropped, the move it is part of, and how many lines it adds to the change's size.
/// </summary>
internal sealed record RoutedFile(
    ChangedFile Change,
    FileRoleAt? AtBase,
    FileRoleAt? AtHead,
    IReadOnlyList<RoutedLine> Added,
    IReadOnlyList<RoutedLine> Removed,
    FileMove? Move,
    int CountedLines)
{
    public string Path => Change.Path;

    public FileRole Role => (AtBase ?? AtHead)!.Role;

    public string? Project => (AtBase ?? AtHead)!.Project;
}
