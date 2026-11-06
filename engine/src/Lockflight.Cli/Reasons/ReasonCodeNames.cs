namespace Lockflight.Cli.Reasons;

/// <summary>The contract spellings of <see cref="ReasonCode"/> (<c>reason</c> in evidence and results), in one place.</summary>
internal static class ReasonCodeNames
{
    private static readonly (ReasonCode Code, string Name)[] Names =
    [
        (ReasonCode.RedMissing, "RED_MISSING"),
        (ReasonCode.TooManyRedCommits, "TOO_MANY_RED_COMMITS"),
        (ReasonCode.CompileRed, "COMPILE_RED"),
        (ReasonCode.RedAddsNoTests, "RED_ADDS_NO_TESTS"),
        (ReasonCode.RedWeakensSuite, "RED_WEAKENS_SUITE"),
        (ReasonCode.RedOther, "RED_OTHER"),
        (ReasonCode.RedInTest, "RED_IN_TEST"),
        (ReasonCode.RedNoProduction, "RED_NO_PRODUCTION"),
        (ReasonCode.GreenBefore, "GREEN_BEFORE"),
        (ReasonCode.Skipped, "SKIPPED"),
        (ReasonCode.Flaky, "FLAKY"),
        (ReasonCode.NoAcceptedCycle, "NO_ACCEPTED_CYCLE"),
        (ReasonCode.AmendRejected, "AMEND_REJECTED"),
        (ReasonCode.AmendWeakened, "AMEND_WEAKENED"),
        (ReasonCode.AmendUncheckable, "AMEND_UNCHECKABLE"),
        (ReasonCode.AmendChangedExpectation, "AMEND_CHANGED_EXPECTATION"),
        (ReasonCode.CycleTestMissing, "CYCLE_TEST_MISSING"),
        (ReasonCode.ExistingTestPassesOnBase, "EXISTING_TEST_PASSES_ON_BASE"),
        (ReasonCode.ExistingTestUncheckable, "EXISTING_TEST_UNCHECKABLE"),
        (ReasonCode.HostTampering, "HOST_TAMPERING"),
        (ReasonCode.HeadDoesNotBuild, "HEAD_DOES_NOT_BUILD"),
        (ReasonCode.TestsNotListed, "TESTS_NOT_LISTED"),
        (ReasonCode.HeadNotGreen, "HEAD_NOT_GREEN"),
        (ReasonCode.BaseNotCompared, "BASE_NOT_COMPARED"),
        (ReasonCode.NoTestsRan, "NO_TESTS_RAN"),
        (ReasonCode.GreenUndecided, "GREEN_UNDECIDED"),
        (ReasonCode.SpecUnresolved, "SPEC_UNRESOLVED"),
        (ReasonCode.SpecDuplicateRequirement, "SPEC_DUPLICATE_REQUIREMENT"),
        (ReasonCode.SpecNoRequirements, "SPEC_NO_REQUIREMENTS"),
        (ReasonCode.SpecRequirementUnknown, "SPEC_REQUIREMENT_UNKNOWN"),
        (ReasonCode.TraceUncovered, "TRACE_UNCOVERED"),
        (ReasonCode.SpecNotOnBase, "SPEC_NOT_ON_BASE"),
        (ReasonCode.SpecChangedInPullRequest, "SPEC_CHANGED_IN_PULL_REQUEST"),
        (ReasonCode.CodeownersMissing, "CODEOWNERS_MISSING"),
        (ReasonCode.RoutingRuleBroken, "ROUTING_RULE_BROKEN"),
        (ReasonCode.PolicyLoosened, "POLICY_LOOSENED"),
        (ReasonCode.PolicyNotCompared, "POLICY_NOT_COMPARED"),
        (ReasonCode.ReviewNotRun, "REVIEW_NOT_RUN"),
        (ReasonCode.ReviewStale, "REVIEW_STALE"),
        (ReasonCode.ReviewFindings, "REVIEW_FINDINGS"),
        (ReasonCode.CiBudgetExhausted, "CI_BUDGET_EXHAUSTED"),
        (ReasonCode.JudgeDidNotFinish, "JUDGE_DID_NOT_FINISH"),
        (ReasonCode.EvidenceInvalid, "EVIDENCE_INVALID"),
        (ReasonCode.NotJudgedYet, "NOT_JUDGED_YET"),
        (ReasonCode.GatesFailed, "GATES_FAILED"),
        (ReasonCode.ToolError, "TOOL_ERROR"),
        (ReasonCode.BuildFailed, "BUILD_FAILED"),
        (ReasonCode.NewBuildWarnings, "NEW_BUILD_WARNINGS"),
        (ReasonCode.FormatNeeded, "FORMAT_NEEDED"),
        (ReasonCode.NewSuppressions, "NEW_SUPPRESSIONS"),
        (ReasonCode.DocsReferencesBroken, "DOCS_REFERENCES_BROKEN"),
        (ReasonCode.EfMigrationMissing, "EF_MIGRATION_MISSING"),
        (ReasonCode.EfCheckFailed, "EF_CHECK_FAILED"),
        (ReasonCode.TestsFailed, "TESTS_FAILED"),
        (ReasonCode.TestRunBroken, "TEST_RUN_BROKEN"),
        (ReasonCode.TestSettingsInvalid, "TEST_SETTINGS_INVALID"),
        (ReasonCode.CoverageReportMissing, "COVERAGE_REPORT_MISSING"),
        (ReasonCode.PatchCoverageLow, "PATCH_COVERAGE_LOW"),
        (ReasonCode.VulnerablePackage, "VULNERABLE_PACKAGE"),
        (ReasonCode.SecretFound, "SECRET_FOUND"),
        (ReasonCode.SecretAllowedInline, "SECRET_ALLOWED_INLINE"),
        (ReasonCode.GitleaksMissing, "GITLEAKS_MISSING"),
        (ReasonCode.GitHistoryMissing, "GIT_HISTORY_MISSING"),
        (ReasonCode.GitUnavailable, "GIT_UNAVAILABLE"),
        (ReasonCode.StepTimedOut, "STEP_TIMED_OUT"),
        (ReasonCode.EnvironmentProblem, "ENVIRONMENT_PROBLEM"),
        (ReasonCode.ScannerReplaced, "SCANNER_REPLACED"),
        (ReasonCode.EngineLockRefused, "ENGINE_LOCK_REFUSED"),
        (ReasonCode.NeedsWindowsRunner, "NEEDS_WINDOWS_RUNNER"),
    ];

    /// <summary>Every code that has a contract name: all but <see cref="ReasonCode.None"/> and <see cref="ReasonCode.Unknown"/>.</summary>
    public static IReadOnlyList<ReasonCode> Named { get; } = [.. Names.Select(entry => entry.Code)];

    /// <summary>The contract name; null for <see cref="ReasonCode.None"/> and <see cref="ReasonCode.Unknown"/>, which are never written.</summary>
    public static string? ToName(ReasonCode code) =>
        Names.FirstOrDefault(entry => entry.Code == code).Name;

    /// <summary>The code a contract name spells; <see cref="ReasonCode.Unknown"/> for a name this engine does not know, or none.</summary>
    public static ReasonCode Parse(string? name) =>
        Names.FirstOrDefault(entry => entry.Name == name) is { Name: not null } entry ? entry.Code : ReasonCode.Unknown;

    /// <summary>
    /// <paramref name="reason"/>, when a result with this outcome may carry it: a result that does not pass names why, so a refusal
    /// without a code is a bug in the engine, never a reason-less result.
    /// </summary>
    public static ReasonCode Required(bool refused, ReasonCode reason) =>
        refused && reason == ReasonCode.None
            ? throw new ArgumentException("a result that does not pass names a reason code", nameof(reason))
            : reason;
}
