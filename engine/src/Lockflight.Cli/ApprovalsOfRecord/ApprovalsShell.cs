namespace Lockflight.Cli.ApprovalsOfRecord;

/// <summary>
/// The workspace shell that holds a unit repository's specs, as the approvals check reads it: the read-only clone, the shell commit
/// (its branch's tip, so a spec there is merged) and the shell's <c>approvals.specPaths</c>.
/// </summary>
internal sealed record ApprovalsShell(string Root, string Commit, IReadOnlyList<string> SpecPaths);
