namespace Lockflight.Cli.Config.Migration;

/// <summary>
/// The migration's outcome: the new text when <see cref="ManifestMigrationStatus.Migrated"/>; for a manual edit, the reason and the
/// snippet to add; the checks version 2 added, which the migrated manifest has only report.
/// </summary>
internal sealed record ManifestMigrationResult(ManifestMigrationStatus Status, string? Text, string Message)
{
    /// <summary>What the person adds by hand when the file cannot be edited (<see cref="ManifestMigrationStatus.ManualEdit"/>).</summary>
    public string? Snippet { get; init; }

    /// <summary>The checks version 2 added, written as <c>report</c> (or <c>auto</c>), each with how to make it fail.</summary>
    public IReadOnlyList<string> NewChecks { get; init; } = [];
}
