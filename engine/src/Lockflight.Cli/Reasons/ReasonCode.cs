namespace Lockflight.Cli.Reasons;

/// <summary>
/// Why a CI check or a gate step did not pass: a stable code (<c>reason</c> in the CI evidence and the gate result) that a person
/// can look up and a tool can key on, beside the free-text line that gives the details. The set grows by addition; a value is never
/// renumbered or reused.
/// </summary>
internal enum ReasonCode
{
    /// <summary>No reason: the result passed or did not apply.</summary>
    None = 0,

    /// <summary>A code read from evidence that this engine does not know, or none where one was expected; it decides nothing.</summary>
    Unknown = 1,

    /// <summary>The pull request has no commit with a <c>Lockflight-Red</c> trailer (<c>RED_MISSING</c>).</summary>
    RedMissing = 2,

    /// <summary>More red commits than <c>ci.maxRedCommits</c> allows (<c>TOO_MANY_RED_COMMITS</c>).</summary>
    TooManyRedCommits = 3,

    /// <summary>The tests do not compile at a red commit (<c>COMPILE_RED</c>).</summary>
    CompileRed = 4,

    /// <summary>A red commit adds no new test (<c>RED_ADDS_NO_TESTS</c>).</summary>
    RedAddsNoTests = 5,

    /// <summary>A red commit deletes, skips or makes explicit a test that existed before it (<c>RED_WEAKENS_SUITE</c>).</summary>
    RedWeakensSuite = 6,

    /// <summary>A new test fails with another exception, or a <c>NotImplementedException</c> from outside production code (<c>RED_OTHER</c>).</summary>
    RedOther = 7,

    /// <summary>A new test fails in its own code or setup (<c>RED_IN_TEST</c>).</summary>
    RedInTest = 8,

    /// <summary>A new test fails on an assertion without calling production code (<c>RED_NO_PRODUCTION</c>).</summary>
    RedNoProduction = 9,

    /// <summary>A new test passes before the implementation exists (<c>GREEN_BEFORE</c>).</summary>
    GreenBefore = 10,

    /// <summary>A new test is skipped or did not run (<c>SKIPPED</c>).</summary>
    Skipped = 11,

    /// <summary>Repeated runs of a test disagree (<c>FLAKY</c>).</summary>
    Flaky = 12,

    /// <summary>No red/green cycle was accepted, so there is nothing to compare with (<c>NO_ACCEPTED_CYCLE</c>).</summary>
    NoAcceptedCycle = 13,

    /// <summary>A cycle test changed after its red and no longer fails on the red commit's code (<c>AMEND_REJECTED</c>).</summary>
    AmendRejected = 14,

    /// <summary>A cycle test changed after its red and checks fewer rows or assertions (<c>AMEND_WEAKENED</c>).</summary>
    AmendWeakened = 15,

    /// <summary>A cycle test changed after its red and its test files cannot be built or run on the red commit's code (<c>AMEND_UNCHECKABLE</c>).</summary>
    AmendUncheckable = 16,

    /// <summary>A cycle test changed after its red and its expectation changed with it (<c>AMEND_CHANGED_EXPECTATION</c>).</summary>
    AmendChangedExpectation = 17,

    /// <summary>A test that went red is gone at head: deleted or renamed (<c>CYCLE_TEST_MISSING</c>).</summary>
    CycleTestMissing = 18,

    /// <summary>An existing test the pull request changed passes on the base's code (<c>EXISTING_TEST_PASSES_ON_BASE</c>).</summary>
    ExistingTestPassesOnBase = 19,

    /// <summary>An existing test the pull request changed cannot be built or run on the base's code (<c>EXISTING_TEST_UNCHECKABLE</c>).</summary>
    ExistingTestUncheckable = 20,

    /// <summary>New code in the test assemblies could tamper with the test host (<c>HOST_TAMPERING</c>).</summary>
    HostTampering = 21,

    /// <summary>The pull request's head does not build (<c>HEAD_DOES_NOT_BUILD</c>).</summary>
    HeadDoesNotBuild = 22,

    /// <summary>A tree's tests could not be listed (<c>TESTS_NOT_LISTED</c>).</summary>
    TestsNotListed = 23,

    /// <summary>The suite at head has failures, or tests deleted, skipped or made explicit since the base (<c>HEAD_NOT_GREEN</c>).</summary>
    HeadNotGreen = 24,

    /// <summary>The base could not be listed or run to compare with (<c>BASE_NOT_COMPARED</c>).</summary>
    BaseNotCompared = 25,

    /// <summary>No test ran (<c>NO_TESTS_RAN</c>).</summary>
    NoTestsRan = 26,

    /// <summary>Green at head was not decided, so trace cannot be (<c>GREEN_UNDECIDED</c>).</summary>
    GreenUndecided = 27,

    /// <summary>The feature's spec could not be found or read (<c>SPEC_UNRESOLVED</c>).</summary>
    SpecUnresolved = 28,

    /// <summary>The spec defines a requirement id more than once with a marker on one line (<c>SPEC_DUPLICATE_REQUIREMENT</c>).</summary>
    SpecDuplicateRequirement = 29,

    /// <summary>The spec defines no requirement (<c>SPEC_NO_REQUIREMENTS</c>).</summary>
    SpecNoRequirements = 30,

    /// <summary>The tests name requirements the spec does not define (<c>SPEC_REQUIREMENT_UNKNOWN</c>).</summary>
    SpecRequirementUnknown = 31,

    /// <summary>A requirement is not named by a test that went red, then green (<c>TRACE_UNCOVERED</c>).</summary>
    TraceUncovered = 32,

    /// <summary>Spec-first applies and the feature's spec is not on the target branch (<c>SPEC_NOT_ON_BASE</c>).</summary>
    SpecNotOnBase = 33,

    /// <summary>Spec-first applies and the pull request changes a spec already on the target branch (<c>SPEC_CHANGED_IN_PULL_REQUEST</c>).</summary>
    SpecChangedInPullRequest = 34,

    /// <summary>The target branch's CODEOWNERS does not give the spec paths an owner (<c>CODEOWNERS_MISSING</c>).</summary>
    CodeownersMissing = 35,

    /// <summary>A routing rule of the target branch fails, such as a move out of a critical path (<c>ROUTING_RULE_BROKEN</c>).</summary>
    RoutingRuleBroken = 36,

    /// <summary>The pull request's <c>lockflight.yaml</c> is looser than the target branch's (<c>POLICY_LOOSENED</c>).</summary>
    PolicyLoosened = 37,

    /// <summary>The pull request's <c>lockflight.yaml</c> could not be compared with the target branch's (<c>POLICY_NOT_COMPARED</c>).</summary>
    PolicyNotCompared = 38,

    /// <summary>No review ran for this head (<c>REVIEW_NOT_RUN</c>).</summary>
    ReviewNotRun = 39,

    /// <summary>The review that ran is about another head (<c>REVIEW_STALE</c>).</summary>
    ReviewStale = 40,

    /// <summary>The review of record did not pass: confirmed blocking findings, or findings it could not confirm (<c>REVIEW_FINDINGS</c>).</summary>
    ReviewFindings = 41,

    /// <summary>The judge's time budget (<c>ci.timeoutMinutes</c>) ran out before the check was decided (<c>CI_BUDGET_EXHAUSTED</c>).</summary>
    CiBudgetExhausted = 42,

    /// <summary>The judge did not finish: its evidence is missing (<c>JUDGE_DID_NOT_FINISH</c>).</summary>
    JudgeDidNotFinish = 43,

    /// <summary>The judge's evidence or the review's result is about another head, has the wrong shape or disagrees with git (<c>EVIDENCE_INVALID</c>).</summary>
    EvidenceInvalid = 44,

    /// <summary>This engine version does not judge the check yet (<c>NOT_JUDGED_YET</c>).</summary>
    NotJudgedYet = 45,

    /// <summary>A gate step did not pass and its result names no reason (<c>GATES_FAILED</c>).</summary>
    GatesFailed = 46,

    /// <summary>A tool Lockflight runs (git, dotnet, a test runner, gitleaks) failed or gave output it could not read (<c>TOOL_ERROR</c>).</summary>
    ToolError = 47,

    /// <summary>The build failed (<c>BUILD_FAILED</c>).</summary>
    BuildFailed = 48,

    /// <summary>The build has warnings its baseline does not accept (<c>NEW_BUILD_WARNINGS</c>).</summary>
    NewBuildWarnings = 49,

    /// <summary>Changed lines are not formatted as <c>dotnet format</c> would format them (<c>FORMAT_NEEDED</c>).</summary>
    FormatNeeded = 50,

    /// <summary>The change adds analyzer suppressions its baseline does not accept (<c>NEW_SUPPRESSIONS</c>).</summary>
    NewSuppressions = 51,

    /// <summary>The change breaks a reference in an instruction file (<c>DOCS_REFERENCES_BROKEN</c>).</summary>
    DocsReferencesBroken = 52,

    /// <summary>An EF Core model changed without a migration (<c>EF_MIGRATION_MISSING</c>).</summary>
    EfMigrationMissing = 53,

    /// <summary><c>dotnet ef</c> could not check a model for pending changes (<c>EF_CHECK_FAILED</c>).</summary>
    EfCheckFailed = 54,

    /// <summary>Tests failed, or did not complete, beyond the known-failing baseline (<c>TESTS_FAILED</c>).</summary>
    TestsFailed = 55,

    /// <summary>The test run broke: no report, an incomplete run or an exit code that is not a test verdict (<c>TEST_RUN_BROKEN</c>).</summary>
    TestRunBroken = 56,

    /// <summary>The test settings in <c>lockflight.yaml</c> cannot run (<c>TEST_SETTINGS_INVALID</c>).</summary>
    TestSettingsInvalid = 57,

    /// <summary>The test run wrote no coverage report (<c>COVERAGE_REPORT_MISSING</c>).</summary>
    CoverageReportMissing = 58,

    /// <summary>The changed lines' coverage is below the minimum (<c>PATCH_COVERAGE_LOW</c>).</summary>
    PatchCoverageLow = 59,

    /// <summary>A package with a new High or Critical advisory (<c>VULNERABLE_PACKAGE</c>).</summary>
    VulnerablePackage = 60,

    /// <summary>gitleaks found a possible secret (<c>SECRET_FOUND</c>).</summary>
    SecretFound = 61,

    /// <summary>Added lines silence gitleaks with an inline allow comment (<c>SECRET_ALLOWED_INLINE</c>).</summary>
    SecretAllowedInline = 62,

    /// <summary>gitleaks is not installed, so secrets cannot be scanned (<c>GITLEAKS_MISSING</c>).</summary>
    GitleaksMissing = 63,

    /// <summary>git could not compare with the base: a shallow checkout, or a ref a repository does not have (<c>GIT_HISTORY_MISSING</c>).</summary>
    GitHistoryMissing = 64,

    /// <summary>The step needs a git repository, and git could not read it (<c>GIT_UNAVAILABLE</c>).</summary>
    GitUnavailable = 65,

    /// <summary>A step's tool ran out of the gates' time limit (<c>STEP_TIMED_OUT</c>).</summary>
    StepTimedOut = 66,

    /// <summary>The machine lacks something a tool needs: a runtime, a toolchain, the network, Docker (<c>ENVIRONMENT_PROBLEM</c>).</summary>
    EnvironmentProblem = 67,

    /// <summary>
    /// The secrets scanner is not the binary the CI template installed: its SHA-256 differs, so something replaced it after the
    /// install (<c>SCANNER_REPLACED</c>).
    /// </summary>
    ScannerReplaced = 68,

    /// <summary>
    /// The CI action's engine lock check refused the pull request's engine lock before the judge ran: the lock does not match the
    /// release's attested hashes, or it could not be checked (<c>ENGINE_LOCK_REFUSED</c>).
    /// </summary>
    EngineLockRefused = 69,

    /// <summary>
    /// A test project needs Windows (a <c>net*-windows</c> framework, WPF or Windows Forms) and the judge runs on another OS, so
    /// nothing was built (<c>NEEDS_WINDOWS_RUNNER</c>).
    /// </summary>
    NeedsWindowsRunner = 70,

    /// <summary>
    /// In a structural-only change, the base's version of an existing test the pull request changed fails on the head's code: the
    /// change altered behaviour that test pinned (<c>EXISTING_TEST_OLD_VERSION_FAILS</c>).
    /// </summary>
    ExistingTestOldVersionFails = 71,

    /// <summary>
    /// In a structural-only change, the base's version of an existing test the pull request changed does not build or run on the
    /// head's code (<c>EXISTING_TEST_OLD_VERSION_UNCHECKABLE</c>).
    /// </summary>
    ExistingTestOldVersionUncheckable = 72,

    /// <summary>
    /// A structural-only change adds a test that names no structural requirement of its spec: a new test pins new behaviour
    /// (<c>STRUCTURAL_CHANGE_NEW_TEST</c>).
    /// </summary>
    StructuralChangeNewTest = 73,

    /// <summary>
    /// A process the pull request's build or tests started was still running after the engine tried to stop it (another user's
    /// process, typically started through <c>sudo</c>), so nothing the judge read afterwards can be trusted (<c>SURVIVOR_NOT_STOPPED</c>).
    /// </summary>
    SurvivorNotStopped = 74,

    /// <summary>
    /// A note, never a verdict: a process kept a build's or a test run's output open after the process the engine started had exited,
    /// so the engine stopped waiting for it (<c>SURVIVOR_OUTPUT_HELD</c>).
    /// </summary>
    SurvivorOutputHeld = 75,
}
