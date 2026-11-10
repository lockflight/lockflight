using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Reasons;

/// <summary>
/// What a person does about each <see cref="ReasonCode"/>: a short "Next" line for the PR comment and the console, and the
/// entry in <c>docs/guides/reasons.md</c> that explains it. The line is advice derived from the code alone, so it never names a
/// value from the pull request; the check's own detail line gives those.
/// </summary>
internal static class ReasonCatalogue
{
    public const string GuideUrl = "https://github.com/lockflight/lockflight/blob/main/docs/guides/reasons.md";

    private const string RunAgain = "re-run the check; if it repeats, read the job log for the tool's own error (the code was not judged)";
    private const string Amend = "a person amends it (`/lockflight:amend <Class.Method> --reason <why>` in Claude Code, or `redgreen amend` in a terminal) and `redgreen red` re-checks it";

    /// <summary>The Next line; empty for <see cref="ReasonCode.None"/> and <see cref="ReasonCode.Unknown"/>, which have no entry.</summary>
    public static string Next(ReasonCode code) => code switch
    {
        ReasonCode.RedMissing => "Commit each story's new tests, with stubs for what does not exist yet, before the implementation, as a commit with the trailer `Lockflight-Red: 1` (2 for the next story); `redgreen red` makes that commit for you.",
        ReasonCode.TooManyRedCommits => "Split the pull request so each has at most `ci.maxRedCommits` red commits, or raise that limit on the target branch.",
        ReasonCode.CompileRed => "Make the red commit compile: declare every member its tests use as a stub that throws `NotImplementedException`, in the same commit as the tests.",
        ReasonCode.RedAddsNoTests => "Put the story's new tests in the red commit, or remove the `Lockflight-Red` trailer from a commit that adds none.",
        ReasonCode.RedWeakensSuite => "Keep the existing tests in the red commit as they were: a red adds tests; it does not delete, skip or make explicit the ones before it.",
        ReasonCode.RedOther => Sentence(RedClassFixes.For(RedClass.RedOther)),
        ReasonCode.RedInTest => Sentence(RedClassFixes.For(RedClass.RedInTest)),
        ReasonCode.RedNoProduction => Sentence(RedClassFixes.For(RedClass.RedNoProduction)),
        ReasonCode.GreenBefore => "Commit the tests before the code: the red commit holds the tests and stubs only, the implementation comes in later commits (reorder the branch if it went in first); a test of behaviour that already exists is no red, so make it describe what is still missing.",
        ReasonCode.Skipped => Sentence(RedClassFixes.For(RedClass.Skipped)),
        ReasonCode.Flaky => "Make the test deterministic (time, order, randomness, shared state), then push again; a cycle test must pass on its first run.",
        ReasonCode.NoAcceptedCycle => "Fix the red check first: tests since red compares with the cycles it accepted.",
        ReasonCode.AmendRejected => $"Restore the test as it was at its red: it now passes on the red commit's code. If the test was wrong, {Amend}, then push its new red.",
        ReasonCode.AmendWeakened => "Restore the rows and assertions the test had at its red; an amend may change what a test checks, not drop part of it.",
        ReasonCode.AmendUncheckable => "Make the test side build on the red commit's code: amend a test before the next cycle starts, and keep each story's tests in new test files of their own.",
        ReasonCode.AmendChangedExpectation => "The expectation changed after red: add a new red commit for that cycle (`Lockflight-Red: <cycle>`) whose tests fail with the new expectation, or restore the test as it was at red.",
        ReasonCode.CycleTestMissing => "Keep the name each test went red with until the pull request merges: restore a deleted or renamed cycle test.",
        ReasonCode.ExistingTestPassesOnBase => "The changed test no longer tells the old behaviour from the new: the person who merges decides whether that is a refactor or a lost check (advisory).",
        ReasonCode.ExistingTestUncheckable => "The changed test's side does not build or run on the base's code; the person who merges reviews the change by reading it (advisory).",
        ReasonCode.HostTampering => "Remove the listed code from the test assemblies, or a person accepts a legitimate finding by merging its baseline entry into `.lockflight/baselines/host-tampering.json` on the target branch first.",
        ReasonCode.HeadDoesNotBuild => "Make the pull request's head build: run `dotnet build` and fix the first error.",
        ReasonCode.TestsNotListed => "Make every test project listable: it must build, and NUnit or MSTest on Microsoft.Testing.Platform needs `Microsoft.Testing.Extensions.TrxReport` (VSTest: `Microsoft.NET.Test.Sdk`).",
        ReasonCode.HeadNotGreen => "Make the whole suite pass at head with nothing deleted, skipped or made explicit since the base; the detail names each problem.",
        ReasonCode.BaseNotCompared => "The base could not be built, listed or run to compare with; fix the target branch's build, or re-run the check if a tool broke.",
        ReasonCode.NoTestsRan => "Make tests run: check that the test projects are found and that `test.stopFilter` in `lockflight.yaml` matches tests that are not skipped.",
        ReasonCode.GreenUndecided => "Fix green at head first: trace counts only tests that pass there.",
        ReasonCode.SpecUnresolved => "Name the branch after the feature (`specs/<id>/`, as Spec Kit does) and make sure its `spec.md` exists and is readable.",
        ReasonCode.SpecDuplicateRequirement => "Give each requirement one line in `spec.md`: an id with a marker must not be defined twice.",
        ReasonCode.SpecNoRequirements => "Write the feature's requirements in `spec.md` as `FR-001 …` lines (see the spec contract).",
        ReasonCode.SpecRequirementUnknown => "Make the tests name requirement ids the spec defines, or add the requirement to the spec first.",
        ReasonCode.TraceUncovered => "Name each listed requirement in a test that went red, then green (`[Trait(\"Requirement\", \"FR-001\")]`, or the NUnit or MSTest property), in a red commit.",
        ReasonCode.SpecNotOnBase => "Merge the feature's spec into the target branch in its own pull request first, then run this check again.",
        ReasonCode.SpecChangedInPullRequest => "Take the spec change out of this pull request and merge it on its own first: under spec-first the approved spec does not change with the code.",
        ReasonCode.CodeownersMissing => "Give the spec paths an owner in CODEOWNERS on the target branch, and require code-owner review on the platform.",
        ReasonCode.RoutingRuleBroken => "Undo what the routing rule forbids (the detail names it), or a person changes the routing policy on the target branch.",
        ReasonCode.PolicyLoosened => "Tighten `lockflight.yaml` back to the target branch's rules; a looser policy is a person's decision, merged on its own.",
        ReasonCode.PolicyNotCompared => "Make `lockflight.yaml` in the pull request readable so it can be compared with the target branch's.",
        ReasonCode.ReviewNotRun => "Give the CI review job its API key (a spend-limited key scoped to CI) and run the check again; a pull request from a fork is reviewed another way.",
        ReasonCode.ReviewStale => "Re-run the review job for the current head.",
        ReasonCode.ReviewFindings => "Fix the review's confirmed findings (the review job's summary lists them) and push; a person dismisses a wrong one with `review dismiss`.",
        ReasonCode.CiBudgetExhausted => "Raise `ci.timeoutMinutes` on the target branch (and the CI job's own time limit with it), or split the pull request.",
        ReasonCode.JudgeDidNotFinish => "Read the judge job's annotations first: when the engine's lock check refused the engine pin, re-running does not help, so fix what it names. Otherwise re-run the workflow: the judge job was cancelled, killed at the platform's time limit, or failed to upload its evidence.",
        ReasonCode.EvidenceInvalid => "Re-run the workflow from the start; if it repeats, the job log shows what in the evidence disagreed (the code was not judged).",
        ReasonCode.NotJudgedYet => "Update the engine pin to a version that judges this check, or turn the check off in the target branch's policy.",
        ReasonCode.GatesFailed => "Open the gates result in the evidence: the first step that did not pass says what failed.",
        ReasonCode.ToolError => $"A tool broke, not the code: {RunAgain}.",
        ReasonCode.BuildFailed => "Fix the build error the detail names (`dotnet build` shows it in full).",
        ReasonCode.NewBuildWarnings => "Fix the new warnings; a person accepts today's warnings once with `baseline build`, merged in its own pull request.",
        ReasonCode.FormatNeeded => "Format the changed lines the detail lists (`dotnet format` fixes them, but also rewrites the rest of each file).",
        ReasonCode.NewSuppressions => "Fix what the new suppressions silence; a person accepts a needed one, by file and rule, with `baseline suppressions`.",
        ReasonCode.DocsReferencesBroken => "Fix each reference the change broke in the instruction files (CLAUDE.md, AGENTS.md, .claude/), or restore what it names.",
        ReasonCode.EfMigrationMissing => "Add a migration for each changed model (`dotnet ef migrations add <name>`) and commit it with the change.",
        ReasonCode.EfCheckFailed => "Make the model checkable: EF Core 8 or later, and a startup project that references `Microsoft.EntityFrameworkCore.Design`; or set `gates.ef.pendingModelChanges: off`.",
        ReasonCode.TestsFailed => "Fix the failing tests the detail names; a failure that predates the change is accepted by a person with `baseline tests`, merged first.",
        ReasonCode.TestRunBroken => "Run `dotnet test` to see why the run broke (a test host crashed, a project did not run, no report was written) and fix that.",
        ReasonCode.TestSettingsInvalid => "Fix the test settings in `lockflight.yaml` the detail names (`test.stopFilter`, `gates.test.framework`).",
        ReasonCode.CoverageReportMissing => "Reference the coverage collector's package from every test project (`doctor` lists the ones missing it), or set `gates.coverage.collector: none`.",
        ReasonCode.PatchCoverageLow => "Cover the changed lines the detail lists with tests, up to `gates.coverage.patchThreshold`.",
        ReasonCode.VulnerablePackage => "Update each listed package to a version without the advisory.",
        ReasonCode.SecretFound => "Remove the secret and rotate it; a person accepts a false positive in `.gitleaksignore`.",
        ReasonCode.SecretAllowedInline => "Remove the inline `gitleaks:allow` comments; a person accepts a false positive in `.gitleaksignore` instead.",
        ReasonCode.GitleaksMissing => "Install gitleaks on `PATH`, or set `LOCKFLIGHT_GITLEAKS` to its path.",
        ReasonCode.GitHistoryMissing => "Fetch the full history (`fetch-depth: 0` in the checkout) so `--base` is reachable in every repository.",
        ReasonCode.GitUnavailable => "Run the gates in a git clone with git on `PATH`.",
        ReasonCode.StepTimedOut => "Make the step faster, or raise `gates.timeoutSeconds`; the Stop gate leaves slow tests to CI with `test.stopFilter`.",
        ReasonCode.EnvironmentProblem => "Fix the machine as the detail says (a runtime, a toolchain, network for restore, Docker), then run the gates again.",
        ReasonCode.NeedsWindowsRunner => "Run the judge on windows-latest: a person runs `lockflight update --project`, which moves the GitHub workflow's judge and review jobs there; on another CI platform, run the Lockflight job on a Windows agent.",
        ReasonCode.ScannerReplaced => "Do not re-run: something in the build replaced the secrets scanner after it was installed. A person reviews the pull request's build files (projects, props and targets, scripts) for what replaced it.",
        ReasonCode.EngineLockRefused => "Fix what the engine lock check names (the failed check is in the detail): a lock must match the release's attested hashes. A first lock is checked with the workflow's token: keep the judge's `github-token` input at its default with `contents: read` in the job's permissions, then re-run.",
        ReasonCode.ExistingTestOldVersionFails => "The change altered behaviour the existing test pinned, so it is not structural-only: undo the behaviour change, or take the `[structural]` markers off the requirements that change behaviour and commit a red for them.",
        ReasonCode.ExistingTestOldVersionUncheckable => "Keep the API the old test uses (as a thin shim for this pull request) so its old version builds on the new code, or change the test in a pull request of its own; remove the shim later.",
        ReasonCode.StructuralChangeNewTest => "A structural-only change adds only tests that name its structural requirements: move the new test to a pull request of its own with a red commit, or name a structural requirement in it if it pins behaviour the change keeps.",
        ReasonCode.SurvivorNotStopped => "Do not re-run: a process the pull request's build or tests started could not be stopped (it runs as another user, typically through `sudo`), so this run's verdict cannot be trusted. Remove the code that starts it; the evidence's `processes` names its command line and the step it outlived.",
        ReasonCode.SurvivorOutputHeld => "Nothing blocks: a process the step started kept its output open after it exited, and Lockflight stopped waiting for it. Stop that process in the build or in the test's teardown (dispose it, or kill it); the evidence's `processes` lists it.",
        ReasonCode.ArtifactMismatch => "Re-run the whole workflow. If it repeats, do not merge: a step other than the action's upload wrote or removed the artifact (the detail names it and both digests), so look in the pull request for code that uploads or deletes `lockflight-*` artifacts, in its build, tests or workflow files, or that leaves a symbolic link in the judge's evidence folder (the upload follows it; the digest counts regular files only).",
        _ => string.Empty,
    };

    /// <summary>The entry's anchor in <see cref="GuideUrl"/>: the contract name in lower case, as GitHub makes it from the heading.</summary>
    public static string? Anchor(ReasonCode code) => ReasonCodeNames.ToName(code)?.ToLowerInvariant();

    /// <summary>The link to the code's entry; null for a code without one.</summary>
    public static string? Link(ReasonCode code) => Anchor(code) is string anchor ? $"{GuideUrl}#{anchor}" : null;

    /// <summary>The console line: Next and where to read more; null for a code without an entry.</summary>
    public static string? ConsoleLine(ReasonCode code) =>
        Link(code) is string link ? $"Next ({ReasonCodeNames.ToName(code)}): {Next(code)} See {link}" : null;

    private static string Sentence(string fix) => fix.Length == 0 ? fix : $"{char.ToUpperInvariant(fix[0])}{fix[1..]}.";
}
