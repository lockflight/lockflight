namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// One red → green cycle (typically one user story); open until <see cref="GreenAt"/> is set. <see cref="Amends"/> are the tests
/// a person allowed to change after the red, in the order given. <see cref="Snapshot"/> is the commit object of the tree the red was
/// accepted on (<see cref="Snapshots.RedSnapshots"/>); null when none could be taken, or for a cycle recorded before snapshots.
/// <see cref="TestFiles"/> are the hashes of the locked test projects' files at red (<see cref="TestFileHashes"/>); empty for a cycle
/// recorded before them.
/// </summary>
internal sealed record RedGreenCycle(int Number, DateTimeOffset RedAt, DateTimeOffset? GreenAt, IReadOnlyList<RedGreenTest> Tests)
{
    public IReadOnlyList<RedGreenAmend> Amends { get; init; } = [];

    public string? Snapshot { get; init; }

    public IReadOnlyDictionary<string, string> TestFiles { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool IsOpen => GreenAt is null;

    /// <summary>Amends whose test no red has re-checked yet: their files are writable, and the cycle cannot go green.</summary>
    public IEnumerable<RedGreenAmend> OpenAmends => Amends.Where(amend => amend.IsOpen);
}
