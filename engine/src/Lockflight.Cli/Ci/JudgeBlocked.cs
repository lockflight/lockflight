using Lockflight.Cli.ApprovalsOfRecord;
using Lockflight.Cli.Config;
using Lockflight.Cli.Evidence.Blocked;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The blocked report (<c>lockflight.blocked/v1</c>) of a NOT-EARNED run that waits for a person: a spec to merge first (on the base
/// branch, or in the workspace shell for a unit), CODEOWNERS that does not cover the specs, a time limit the judge ran out of, or
/// (joined by the report) a required review no key ran. Null for any other run: a NOT-EARNED the agent can fix itself (a missing
/// red commit, say) needs no person. Its needs name no command; whoever renders it derives one.
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

        bool shellSpecPending = ShellSpecPending(run.Findings.Trace?.Spec);
        if (Waits(run, CheckId.Approvals) && run.Findings.Approvals is { Blocked: BlockedReasonCode code } approvals
            && OnlyWaitingOn(run.Checks, check => check.Id == CheckId.Approvals || (check.Id == CheckId.Trace && shellSpecPending)))
        {
            return code == BlockedReasonCode.SpecNotOnBase ? SpecFirst(run, approvals.Facts, approvals.Why) : Codeowners(run, approvals.Facts, approvals.Why);
        }

        if (Waits(run, CheckId.Trace) && shellSpecPending && run.Findings.Trace!.Spec is { FeatureId: string feature, Problem: string problem } spec
            && OnlyWaitingOn(run.Checks, check => check.Id is CheckId.Trace or CheckId.Approvals))
        {
            BlockedNeed merge = BlockedNeed.Of(BlockedWho.Codeowner, BlockedAction.Approve,
                $"Review and merge {spec.SpecPath} in the workspace shell {spec.ShellUrl}, then run this check again", humanOnly: true) with { Paths = spec.SpecPath is string path ? [path] : [] };
            return Report(run, BlockedReasonCode.SpecNotOnBase, problem, merge) with { Feature = feature };
        }

        if (run.Findings.Budget is { Exhausted: true } budget && OnlyWaitingOn(run.Checks, check => check.Why.StartsWith(JudgeBudget.WhyStart, StringComparison.Ordinal)))
        {
            BlockedNeed raise = BlockedNeed.Of(BlockedWho.Maintainer, BlockedAction.RaiseLimit,
                "Raise ci.timeoutMinutes on the target branch (and the CI job's own time limit with it)", humanOnly: true) with { Paths = [Manifest.FileName] };
            string notRun = budget.NotRun.Count > 0 ? $"; not run: {string.Join(", ", budget.NotRun)}" : string.Empty;
            return Report(run, BlockedReasonCode.TimeExhausted, $"the judge ran out of ci.timeoutMinutes ({run.Plan.Policy.Ci.TimeoutMinutes}) before it decided every check{notRun}", raise);
        }

        return null;
    }

    /// <summary>
    /// The report of a run that waits only for the review the change's class requires, when no review ran: a maintainer gives the
    /// review job its key (or, for a pull request from a fork, reviews it another way).
    /// </summary>
    public static BlockedReport ForReview(DateTimeOffset at, string head, string? feature)
    {
        BlockedNeed key = BlockedNeed.Of(BlockedWho.Maintainer, BlockedAction.GrantAccess,
            "Give the CI review job its API key (a spend-limited key scoped to CI) and run the check again; this change's class requires a review", humanOnly: true);
        return BlockedReport.Of(at, Source, BlockedReasonCode.AccessNeeded, "the change's class requires a review, and no review ran", [key]) with
        {
            Head = head,
            Feature = feature,
            Resumable = true,
        };
    }

    /// <summary>
    /// Whether every required check that is not decided (NOT-EARNED or SKIPPED) is one only a person can unblock: a report must not
    /// make a relay wait for a person while the agent still has something of its own to fix.
    /// </summary>
    public static bool OnlyWaitingOn(IEnumerable<CheckResult> checks, Func<CheckResult, bool> onAPerson) =>
        checks.Where(check => check is { Level: CheckLevel.Required, Verdict: CheckVerdict.NotEarned or CheckVerdict.Skipped }).All(onAPerson);

    /// <summary>A unit's feature whose spec the workspace shell does not hold yet (merged later by a person).</summary>
    private static bool ShellSpecPending(SpecResolution? spec) =>
        spec is { Origin: SpecOrigin.Shell, FeatureId: not null, SpecPath: not null, SpecText: null, Problem: not null };

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
