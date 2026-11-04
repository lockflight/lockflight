namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// A file an <c>[outside .NET: …]</c> marker names, what it says about the requirement, and, in CI, whether the pull request added
/// or changed it (<c>added in this pull request</c>, <c>changed in this pull request</c>, <c>unchanged in this pull request</c>; null
/// locally).
/// </summary>
internal sealed record OutsideFile(string Path, OutsideFileState State)
{
    public string? Change { get; init; }
}
