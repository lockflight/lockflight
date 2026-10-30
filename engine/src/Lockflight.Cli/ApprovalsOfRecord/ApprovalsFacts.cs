using Lockflight.Cli.Config;

namespace Lockflight.Cli.ApprovalsOfRecord;

/// <summary>
/// What the CI judge can check about the approval of record, which is the platform's review and is never read: the base branch's
/// CODEOWNERS covers the spec paths, and, where spec-first applies, the spec was merged first and is unchanged. <see cref="Problems"/>
/// says what a person must fix; <see cref="Check"/> says whether those problems fail the judge or are only reported.
/// </summary>
internal sealed record ApprovalsFacts(ApprovalCheckMode Check, ApprovalMode Mode, CodeownersCoverage Codeowners, SpecFirstFacts SpecFirst, IReadOnlyList<string> Problems)
{
    public bool Satisfied => Problems.Count == 0;
}
