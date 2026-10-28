namespace Lockflight.Cli.Docs;

/// <summary>
/// The outcome of a docs drift scan. <see cref="Scanned"/> is false outside a git repository, where there is no list of tracked
/// files to scan. <see cref="Notes"/> say what was not checked (a shallow clone, a history read that ran out of time).
/// </summary>
internal sealed record DocsDriftReport(bool Scanned, int Documents, IReadOnlyList<DriftFinding> Findings, IReadOnlyList<string> Notes)
{
    public static DocsDriftReport NotARepository { get; } = new(Scanned: false, Documents: 0, [], []);
}
