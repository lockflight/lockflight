using Lockflight.Cli.Config;
using Lockflight.Cli.Reasons;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Each check's verdict from the plan and what building and running found; a check this engine does not judge yet is NOT-EARNED,
/// never a guess. An off check is N/A.
/// </summary>
internal static class JudgeChecks
{
    public const string NotJudgedYet = "not judged by this engine version yet";

    /// <summary>Why red and tests since red are off for a structural-only change.</summary>
    public const string StructuralLaneWhy = "structural-only change: covered by trace and the existing tests (see structuralChange)";

    public static IReadOnlyList<CheckResult> Decide(JudgePlan plan, JudgeFindings findings) =>
        [.. Enum.GetValues<CheckId>().Select(check => Decide(plan, findings, check, plan.Levels[check]))];

    private static CheckResult Decide(JudgePlan plan, JudgeFindings findings, CheckId check, CheckLevel level)
    {
        if (check == CheckId.Approvals && findings.Approvals is { Facts.SpecFirst.Applies: true })
        {
            // Spec-first applies by the manifest (its default, the class, a spec-first path), whatever the class's own level.
            level = CheckLevel.Required;
        }

        if (check == CheckId.Policy && plan.Loosened is null)
        {
            string why = plan.Inputs.Event == JudgeEvent.PullRequest ? "no lockflight.yaml on the base to compare with" : "judged on the pull request";
            return new CheckResult(check, CheckLevel.Off, CheckVerdict.NotApplicable, ReasonCode.None, why);
        }

        if (level == CheckLevel.Off && check is CheckId.Red or CheckId.TestsSinceRed && plan.InStructuralLane)
        {
            return new CheckResult(check, level, CheckVerdict.NotApplicable, ReasonCode.None, StructuralLaneWhy);
        }

        if (level == CheckLevel.Off)
        {
            return new CheckResult(check, level, CheckVerdict.NotApplicable, ReasonCode.None, plan.Inputs.Event == JudgeEvent.PullRequest ? "off for this change class" : "judged on the pull request");
        }

        return check switch
        {
            CheckId.Red when plan.Reds.OverCap => new CheckResult(check, level, CheckVerdict.NotEarned, ReasonCode.TooManyRedCommits,
                $"{plan.Reds.Cycles.Count} red commits, more than ci.maxRedCommits ({plan.Policy.Ci.MaxRedCommits}); split the pull request"),
            CheckId.Red when plan.Reds.Cycles.Count == 0 => new CheckResult(check, level, CheckVerdict.NotEarned, ReasonCode.RedMissing, NoRedWhy(plan)),
            CheckId.Red when findings.Cycles is IReadOnlyList<CycleJudgement> cycles => Red(level, cycles),
            CheckId.TestsSinceRed when plan.Reds.OverCap || plan.Reds.Cycles.Count == 0 => new CheckResult(check, level, CheckVerdict.NotEarned, plan.Reds.OverCap ? ReasonCode.TooManyRedCommits : ReasonCode.RedMissing, "no red commit to compare with"),
            CheckId.Red or CheckId.TestsSinceRed or CheckId.TestsExistingChanged or CheckId.TestsHostTampering or CheckId.Trace or CheckId.Gates or CheckId.Green
                when findings.NotBuilt is { } notBuilt => new CheckResult(check, level, notBuilt.Verdict, notBuilt.Reason, notBuilt.Why),
            CheckId.TestsSinceRed when findings.SinceRed is IReadOnlyList<SinceRedTest> sinceRed => SinceRedCheck(level, sinceRed),
            CheckId.TestsExistingChanged when findings.ExistingChanged is IReadOnlyList<ExistingChangedTest> existing => ExistingChangedCheck(level, existing, plan.InStructuralLane),
            CheckId.TestsHostTampering when findings.Tampering is TamperingJudgement tampering => new CheckResult(check, level, tampering.Verdict, tampering.Reason, tampering.Why),
            CheckId.Trace when findings.Trace is TraceJudgement trace => Trace(plan, level, trace),
            CheckId.Gates when findings.Gates is { } gates => new CheckResult(check, level, gates.Verdict, gates.Reason, gates.Why),
            CheckId.Green when findings.Green is GreenJudgement green => new CheckResult(check, level, green.Verdict, green.Reason, green.Why),
            CheckId.Approvals when findings.Approvals is ApprovalsJudgement approvals => Approvals(level, approvals),
            CheckId.Routing => plan.Class.Classification.Failures.Count > 0
                ? new CheckResult(check, level, CheckVerdict.Fail, ReasonCode.RoutingRuleBroken, string.Join("; ", plan.Class.Classification.Failures))
                : new CheckResult(check, level, CheckVerdict.Pass, ReasonCode.None, $"class {ChangeClassNames.ToName(plan.Class.Classification.Class)} ({plan.Class.Source})"
                    + (plan.Class.Classification.Signals is [ChangeSignal top, ..] ? $": {top.Id} in {top.Where}" : string.Empty)
                    + (plan.Class.ProductionChanged ? string.Empty : "; no production code lines changed, so no red/green or trace")),
            CheckId.Policy when plan.PolicyNotCompared is string notCompared => new CheckResult(check, level, CheckVerdict.NotEarned, ReasonCode.PolicyNotCompared, notCompared),
            CheckId.Policy when plan.Loosened is not null => plan.Loosened.Count == 0
                ? new CheckResult(check, level, CheckVerdict.Pass, ReasonCode.None, "lockflight.yaml is no looser than the base's")
                : new CheckResult(check, level, CheckVerdict.Fail, ReasonCode.PolicyLoosened, LoosenedWhy(plan.Loosened)),
            CheckId.Review => new CheckResult(check, level, CheckVerdict.Skipped, ReasonCode.ReviewNotRun, plan.Inputs.ReviewExpected ? "the report joins the review job's result" : "no review job runs for this pull request"),
            _ => new CheckResult(check, level, CheckVerdict.NotEarned, ReasonCode.NotJudgedYet, NotJudgedYet),
        };
    }

    /// <summary>Every cycle accepted passes; otherwise the worst cycle decides, and its line says why.</summary>
    private static CheckResult Red(CheckLevel level, IReadOnlyList<CycleJudgement> cycles)
    {
        CycleJudgement? worst = cycles.FirstOrDefault(cycle => cycle.Verdict == CheckVerdict.Fail)
            ?? cycles.FirstOrDefault(cycle => cycle.Verdict == CheckVerdict.Error)
            ?? cycles.FirstOrDefault(cycle => cycle.Verdict != CheckVerdict.Pass);
        return worst is null
            ? new CheckResult(CheckId.Red, level, CheckVerdict.Pass, ReasonCode.None, $"{cycles.Count} cycles, {cycles.Sum(cycle => cycle.Tests.Count)} tests, each red in production code")
            : new CheckResult(CheckId.Red, level, worst.Verdict, worst.Reason, $"cycle {worst.Red.Label} ({CommitIds.Short(worst.Red.Commit)}): {worst.Why}"
                + (worst.Verdict == CheckVerdict.Fail ? $"; a new red commit with the same label supersedes it ('lockflight red --redo {worst.Red.Label}')" : string.Empty));
    }

    /// <summary>Any amend rejected or weakened fails; a test missing at head, or an amend that cannot be re-checked or changed the expectation, is NOT-EARNED; else PASS.</summary>
    private static CheckResult SinceRedCheck(CheckLevel level, IReadOnlyList<SinceRedTest> tests)
    {
        if (tests.Count == 0)
        {
            return new CheckResult(CheckId.TestsSinceRed, level, CheckVerdict.NotEarned, ReasonCode.NoAcceptedCycle, "no accepted cycle to compare with");
        }

        SinceRedTest? failed = tests.FirstOrDefault(test => test.State is SinceRed.AmendRejected or SinceRed.AmendWeakened);
        SinceRedTest? uncheckable = tests.FirstOrDefault(test => test.State is SinceRed.MissingAtHead or SinceRed.AmendUncheckable or SinceRed.AmendChangedExpectation);
        if ((failed ?? uncheckable) is SinceRedTest worst)
        {
            return new CheckResult(CheckId.TestsSinceRed, level, failed is null ? CheckVerdict.NotEarned : CheckVerdict.Fail, CheckReasons.Of(worst.State), $"cycle {worst.Cycle}, {worst.Key}: {worst.Why}");
        }

        int unchanged = tests.Count(test => test.State == SinceRed.Unchanged);
        int amended = tests.Count - unchanged;
        return new CheckResult(CheckId.TestsSinceRed, level, CheckVerdict.Pass, ReasonCode.None, $"{unchanged} unchanged, {amended} amend{(amended == 1 ? string.Empty : "s")} accepted");
    }

    /// <summary>
    /// A changed test that no longer tells the behaviours apart, or (in a structural-only change) whose old version fails on the
    /// head's code, fails; one that could not be checked is NOT-EARNED; else PASS.
    /// </summary>
    private static CheckResult ExistingChangedCheck(CheckLevel level, IReadOnlyList<ExistingChangedTest> tests, bool structuralChange)
    {
        ExistingChangedTest? failed = tests.FirstOrDefault(test => test.State is ExistingChange.PassesOnBase or ExistingChange.OldVersionFails);
        ExistingChangedTest? uncheckable = tests.FirstOrDefault(test => test.State is ExistingChange.Uncheckable or ExistingChange.OldVersionUncheckable);
        if ((failed ?? uncheckable) is ExistingChangedTest worst)
        {
            return new CheckResult(CheckId.TestsExistingChanged, level, failed is null ? CheckVerdict.NotEarned : CheckVerdict.Fail, CheckReasons.Of(worst.State), $"{worst.Key}: {worst.Why}");
        }

        return new CheckResult(CheckId.TestsExistingChanged, level, CheckVerdict.Pass, ReasonCode.None,
            tests.Count == 0 ? "no existing test changed"
            : structuralChange ? $"{tests.Count} existing tests changed; the old version of each passes on the head's code"
            : $"{tests.Count} existing tests changed, each still failing on the base's code");
    }

    /// <summary>With <c>approvals.check: report</c> the problems are listed, and the check never decides the verdict.</summary>
    private static CheckResult Approvals(CheckLevel level, ApprovalsJudgement approvals) =>
        new(CheckId.Approvals, approvals.Facts.Check == ApprovalCheckMode.Report && level == CheckLevel.Required ? CheckLevel.Advisory : level, approvals.Verdict, approvals.Reason, approvals.Why);

    /// <summary>
    /// Trace is required when it applies to a class that needs it then (a spec exists), off when it does not; a structural-only
    /// change always needs it.
    /// </summary>
    private static CheckResult Trace(JudgePlan plan, CheckLevel level, TraceJudgement trace)
    {
        CheckLevel resolved = plan.Requirements.LevelOf(RequirementKind.SpecAndTrace) == RequirementLevel.WhenApplicable && !plan.InStructuralLane
            ? trace.Applies ? CheckLevel.Required : CheckLevel.Off
            : level;
        return new CheckResult(CheckId.Trace, resolved, trace.Verdict, trace.Reason, trace.Why);
    }

    /// <summary>
    /// The loosenings in one line when they fit the evidence's cap; otherwise their count, and the full list stays in the evidence's
    /// <c>policy.loosened</c> and the comment's "Policy loosened" section.
    /// </summary>
    private static string LoosenedWhy(IReadOnlyList<string> loosened)
    {
        const string Remedy = "a person tightens it back or merges through the platform";
        string full = $"looser than the base ({loosened.Count}): {string.Join("; ", loosened)}; {Remedy}";
        return full.Length <= CiEvidence.MaxTextLength
            ? full
            : $"looser than the base in {loosened.Count} ways, each listed under policy.loosened; {Remedy}";
    }

    private static string NoRedWhy(JudgePlan plan)
    {
        int malformed = plan.Reds.All.Count(red => red.Malformed is not null);
        string trailers = malformed > 0 ? $" ({malformed} commits carry a malformed Lockflight-Red trailer)" : string.Empty;
        if (plan.StructuralChange is { Unmet: [StructuralUnmet first, ..] })
        {
            // The spec is all [structural], so the condition it misses is the actionable part; every one is in structuralChange.unmet.
            return $"no red commit{trailers}, and the all-[structural] spec does not waive it: {first.Why}";
        }

        return $"no red commit in this pull request{trailers}; before the implementation, commit each story's new tests (stub what does not exist yet) "
            + "with git commit --trailer \"Lockflight-Red: 1\" (2 for the next story); 'lockflight red' or 'redgreen red' does this";
    }
}
