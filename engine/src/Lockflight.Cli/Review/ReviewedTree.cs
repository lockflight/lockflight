namespace Lockflight.Cli.Review;

/// <summary>
/// The changes in the reviewed paths that no commit holds: tracked files changed or staged since HEAD (<see cref="Uncommitted"/>), and
/// files git does not track and does not ignore (<see cref="Untracked"/>), both relative to the project root and '/'-separated.
/// <see cref="Checked"/> is false when git could not list them.
/// </summary>
internal sealed record ReviewedTree(bool Checked, IReadOnlyList<string> Uncommitted, IReadOnlyList<string> Untracked)
{
    public static ReviewedTree Clean { get; } = new(true, [], []);

    public static ReviewedTree Unchecked { get; } = new(false, [], []);

    /// <summary>Whether everything the review reads is committed, so the run reviews HEAD.</summary>
    public bool IsClean => Checked && Uncommitted.Count == 0 && Untracked.Count == 0;
}
