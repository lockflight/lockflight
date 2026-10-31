using Lockflight.Cli.ApprovalsOfRecord;
using Lockflight.Cli.Config;
using Lockflight.Cli.Evidence.Blocked;
using Lockflight.Cli.Paths;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The approvals check in the judge (git only, after the builds): the facts <see cref="ApprovalsChecker"/> shows from the target
/// branch's policy and CODEOWNERS, or, in a unit repository, from the workspace shell that holds the specs. Who approved is the
/// platform's required review and is never read. Missing CODEOWNERS coverage or a spec not merged first (also one this pull request
/// adds) is NOT-EARNED: a person must act. A pull request that changes a spec already merged where spec-first applies FAILs; git that
/// cannot show the facts is an ERROR. Where spec-first applies the check is required whatever the class's level; a workspace shell's
/// CODEOWNERS is looked for at every platform's location (the shell may live on another platform than the unit). The platform's
/// code-owner review fires only on a spec the pull request changes: so where approvals are required and spec-first does not apply,
/// a pull request that changes no spec passes only on the feature's spec merged before (approved when it merged, as spec-first
/// would accept); with neither, no person approved anything, and the check is NOT-EARNED until a spec is added or changed.
/// </summary>
internal static class ApprovalsJudge
{
    private const string SingleSource = "single";

    public static async Task<ApprovalsJudgement?> DecideAsync(JudgePlan plan, JudgeFindings findings, CancellationToken cancellationToken)
    {
        if (plan.Inputs.Event != JudgeEvent.PullRequest || plan.Inputs.Base is not string baseCommit)
        {
            return null;
        }

        ApprovalSettings settings = plan.Policy.Manifest?.Approvals ?? ApprovalSettings.ForVersion1;
        // Spec-first needs the feature the pull request names (its branch or the spec it changes): "the only spec there is" would let
        // any code pull request pass against an old, merged spec.
        string? feature = findings.Trace?.Spec is { FeatureId: string traced, Source: not SingleSource } ? traced : await FeatureAsync(plan, cancellationToken);
        CodeownersPlatform codeowners = plan.Shell is null ? Codeowners(plan.Inputs.Platform) : CodeownersPlatform.Local;
        ApprovalsRequest request = new(plan.Inputs.Root, baseCommit, plan.Inputs.Head, settings, plan.Class.Classification.Class, feature, plan.ChangedFiles, codeowners)
        {
            Shell = plan.Shell is ShellPolicy shell ? new ApprovalsShell(shell.Directory, shell.Commit, shell.Manifest.Approvals.SpecPaths) : null,
        };
        ApprovalsFacts facts = await ApprovalsChecker.CheckAsync(request, cancellationToken);
        string why = string.Join("; ", facts.Problems);
        string? merged = feature is null ? null : $"specs/{feature}/spec.md";
        bool samePrWithoutSpec = facts.Satisfied && !facts.SpecFirst.Applies && plan.Shell is null && plan.Levels.GetValueOrDefault(CheckId.Approvals) == CheckLevel.Required
            && !plan.ChangedFiles.Any(path => settings.SpecPaths.Any(glob => PathRules.Matches(glob, path)));
        if (samePrWithoutSpec && (merged is null || await JudgeGit.BlobOfAsync(plan.Inputs.Root, baseCommit, merged, cancellationToken) is null))
        {
            return new ApprovalsJudgement(facts, CheckVerdict.NotEarned,
                $"the class {ChangeClassNames.ToName(plan.Class.Classification.Class)} needs an approved spec, and this pull request changes none{(merged is null ? " and names no feature" : $" while {merged} is not on the base branch")}: the platform's code-owner review covers spec changes only, so no person approved this behaviour",
                BlockedReasonCode.ApprovalMissing);
        }

        if (samePrWithoutSpec)
        {
            return new ApprovalsJudgement(facts, CheckVerdict.Pass, $"{Passed(facts, inShell: false)}; this pull request changes no spec and relies on {merged}, approved when it merged", Blocked: null);
        }

        return facts switch
        {
            { Codeowners.GitError: not null } => new ApprovalsJudgement(facts, CheckVerdict.Error, why, Blocked: null),
            { Satisfied: true } => new ApprovalsJudgement(facts, CheckVerdict.Pass, Passed(facts, plan.Shell is not null), Blocked: null),
            { SpecFirst: { Applies: true, SpecOnBase: true, ChangedSpecs.Count: > 0 } } => new ApprovalsJudgement(facts, CheckVerdict.Fail, why, Blocked: null),
            { SpecFirst.Satisfied: false } => new ApprovalsJudgement(facts, CheckVerdict.NotEarned, why, BlockedReasonCode.SpecNotOnBase),
            _ => new ApprovalsJudgement(facts, CheckVerdict.NotEarned, why, BlockedReasonCode.CodeownersMissing),
        };
    }

    /// <summary>How each platform reads CODEOWNERS; Azure Repos has none (its required reviewers are a branch policy).</summary>
    public static CodeownersPlatform Codeowners(JudgePlatform platform) => platform switch
    {
        JudgePlatform.GitHub => CodeownersPlatform.GitHub,
        JudgePlatform.GitLab => CodeownersPlatform.GitLab,
        JudgePlatform.Bitbucket => CodeownersPlatform.Bitbucket,
        JudgePlatform.Local => CodeownersPlatform.Local,
        _ => CodeownersPlatform.None,
    };

    /// <summary>The feature when trace did not resolve one: the shell's (a feature not merged there yet included) or the tree's.</summary>
    private static async Task<string?> FeatureAsync(JudgePlan plan, CancellationToken cancellationToken) =>
        plan.Shell is ShellPolicy shell
            ? (await ShellSpecSource.ResolveAsync(plan, shell, plan.Inputs.Branch, [], cancellationToken)).FeatureId
            : await TreeSpecSource.FindAsync(plan.Inputs.Root, plan.Inputs.Head, plan.Inputs.Branch, plan.ChangedFiles, cancellationToken) is (string id, not SingleSource) ? id : null;

    private static string Passed(ApprovalsFacts facts, bool inShell)
    {
        string where = inShell ? "the workspace shell's" : "the base branch's";
        string covered = $"{where} {facts.Codeowners.File} gives {string.Join(", ", facts.Codeowners.Covers)} an owner; the approval of record is the platform's required code-owner review";
        return facts.SpecFirst.Applies ? $"{covered}; spec-first: {facts.SpecFirst.SpecPath} was merged first and is unchanged" : covered;
    }
}
