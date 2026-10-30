using Lockflight.Cli.Config;

namespace Lockflight.Cli.ApprovalsOfRecord;

/// <summary>
/// What the approvals check needs: the clone, the base commit the policy and CODEOWNERS are read from (the target branch's tip),
/// the head commit, the base branch's <c>approvals:</c> settings, the pull request's change class, its feature (null when none
/// was found), the paths it changes since the merge base (both sides of a rename) and how the platform reads CODEOWNERS; in a unit
/// repository of a workspace shell, the shell that holds the specs.
/// </summary>
internal sealed record ApprovalsRequest(
    string Root,
    string Base,
    string Head,
    ApprovalSettings Settings,
    ChangeClass Class,
    string? FeatureId,
    IReadOnlyList<string> ChangedPaths,
    CodeownersPlatform Codeowners)
{
    /// <summary>
    /// In a unit repository of a workspace shell, where the specs live: the shell's clone, commit and spec globs. The CODEOWNERS
    /// coverage and the spec-first facts are then the shell's, and the unit's pull request changes no spec of it.
    /// </summary>
    public ApprovalsShell? Shell { get; init; }
}
