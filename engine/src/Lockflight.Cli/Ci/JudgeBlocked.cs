using Lockflight.Cli.ApprovalsOfRecord;
using Lockflight.Cli.Evidence.Blocked;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The blocked report (<c>lockflight.blocked/v1</c>) of a NOT-EARNED run that waits for a person: a spec to merge first (on the base
/// branch, or in the workspace shell for a unit) or CODEOWNERS that does not cover the specs. Null for any other run: a NOT-EARNED
/// the agent can fix itself (a missing red commit, say) needs no person. Its needs name no command; whoever renders it derives one.
/// </summary>
internal static class JudgeBlocked
{
    private const string Source = "ci";

    public static BlockedReport? For(JudgeRun run)
    {
        if (run.Verdict != CheckVerdict.NotEarned)
        {
            return null;
        }

        if (Waits(run, CheckId.Approvals) && run.Findings.Approvals is { Blocked: BlockedReasonCode code } approvals)
        {
            return code == BlockedReasonCode.SpecNotOnBase ? SpecFirst(run, approvals.Facts, approvals.Why) : Codeowners(run, approvals.Facts, approvals.Why);
        }

        if (Waits(run, CheckId.Trace) && run.Findings.Trace?.Spec is { Origin: SpecOrigin.Shell, FeatureId: string feature, SpecPath: string, SpecText: null, Problem: string problem } spec)
        {
            BlockedNeed merge = BlockedNeed.Of(BlockedWho.Codeowner, BlockedAction.Approve,
                $"Review and merge {spec.SpecPath} in the workspace shell {spec.ShellUrl}, then run this check again", humanOnly: true) with { Paths = spec.SpecPath is string path ? [path] : [] };
            return Report(run, BlockedReasonCode.SpecNotOnBase, problem, merge) with { Feature = feature };
        }

        return null;
    }

    /// <summary>Whether the check is required and NOT-EARNED in this run.</summary>
    private static bool Waits(JudgeRun run, CheckId id) =>
        run.Checks.FirstOrDefault(check => check.Id == id) is { Level: CheckLevel.Required, Verdict: CheckVerdict.NotEarned };

    private static BlockedReport SpecFirst(JudgeRun run, ApprovalsFacts facts, string why)
    {
        string where = run.Plan.Shell is null ? "on the target branch" : "in the workspace shell";
        BlockedNeed need = facts.SpecFirst.SpecPath is string specPath
            ? BlockedNeed.Of(BlockedWho.Codeowner, BlockedAction.Approve, $"Review and merge {specPath} {where} in its own pull request, then run this check again", humanOnly: true) with { Paths = [specPath] }
            : BlockedNeed.Of(BlockedWho.Person, BlockedAction.Approve, $"Name the branch after the feature whose spec was approved {where} (specs/<id>/), or merge that spec first", humanOnly: false);
        return Report(run, BlockedReasonCode.SpecNotOnBase, why, need) with { Feature = FeatureOf(facts) };
    }

    private static BlockedReport Codeowners(JudgeRun run, ApprovalsFacts facts, string why)
    {
        IReadOnlyList<string> locations = ApprovalsJudge.Codeowners(run.Plan.Inputs.Platform).Locations;
        string file = facts.Codeowners.File ?? (locations.Count > 0 ? locations[0] : "CODEOWNERS");
        string unowned = facts.Codeowners.Missing.Count > 0 ? string.Join(", ", facts.Codeowners.Missing) : "the spec paths";
        BlockedNeed need = BlockedNeed.Of(BlockedWho.Maintainer, BlockedAction.FixConfig,
            $"Give {unowned} an owner in {file} and require code-owner review on the platform", humanOnly: true) with { Paths = [file] };
        return Report(run, BlockedReasonCode.CodeownersMissing, why, need);
    }

    private static BlockedReport Report(JudgeRun run, BlockedReasonCode code, string summary, BlockedNeed need) =>
        BlockedReport.Of(run.FinishedAt, Source, code, CiEvidence.Cap(summary), [need]) with { Head = run.Plan.Inputs.Head, Resumable = true };

    private static string? FeatureOf(ApprovalsFacts facts) =>
        facts.SpecFirst.SpecPath is string path && path.Split('/') is [_, string id, _] ? id : null;
}
