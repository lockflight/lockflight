namespace Lockflight.Cli.Ci;

/// <summary>
/// The pull request's changes to the spec paths (<c>approvals.specPaths</c>), shown first in its comment: the changed spec files, the
/// diff text (cut at <see cref="SpecDiffReader.MaxLength"/> characters; <see cref="Truncated"/> then says so) and its line count.
/// </summary>
internal sealed record SpecDiff(IReadOnlyList<string> Paths, string Text, int Lines, bool Truncated);
