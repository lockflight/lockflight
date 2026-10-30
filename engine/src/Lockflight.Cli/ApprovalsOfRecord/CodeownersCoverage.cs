namespace Lockflight.Cli.ApprovalsOfRecord;

/// <summary>
/// Whether the base branch's CODEOWNERS gives the spec paths an owner: the file read (null when the base has none), the commit it
/// was read from, the spec globs every probed path of which has an owner, those with a path that has none (with that path), and
/// why git could not show the facts (null when it could).
/// </summary>
internal sealed record CodeownersCoverage(string? File, string From, IReadOnlyList<string> Covers, IReadOnlyList<string> Missing, string? GitError)
{
    public bool Complete => File is not null && Missing.Count == 0 && GitError is null;
}
