namespace Lockflight.Cli.Ci;

/// <summary>
/// The red commits of a pull request: every commit with a red trailer (<see cref="All"/>, malformed and superseded included), the
/// cycles in topological order, the commits with an amend trailer (reserved), and whether the cycles exceed <c>ci.maxRedCommits</c>.
/// </summary>
internal sealed record RedCommitSelection(IReadOnlyList<RedCommit> All, IReadOnlyList<RedCommit> Cycles, IReadOnlyList<string> AmendCommits, bool OverCap, bool LabelsOutOfOrder);
