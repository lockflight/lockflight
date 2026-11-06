# Reasons

Every check or gate step that does not pass names a **reason code**: a stable name such as `GREEN_BEFORE` or `TRACE_UNCOVERED`,
with a detail line about this run and a **Next** line saying what to do. The PR comment, the job summary, `lockflight ci`,
`ci report` and `gate run` print the Next line and link here; the evidence (`lockflight.ci-evidence/v1`) and the gate result
(`lockflight.result/v1`) carry the code as `reason`. The codes are stable: new ones may be added, none is renamed. This page
explains each one: what it means, why it blocks, and what to do next.

**On this page:** [Red commits](#red-commits) · [Tests since red](#tests-since-red) · [Existing tests changed](#existing-tests-changed) · [Host tampering](#host-tampering) · [Green at head](#green-at-head) · [Trace](#trace) · [Approvals](#approvals) · [Change class and policy](#change-class-and-policy) · [Review](#review) · [The judge's run](#the-judges-run) · [Gates](#gates) · [Blocked reports](#blocked-reports)

## Red commits

The `red` check: each commit with a `Lockflight-Red: <cycle>` trailer is re-run, and its new tests must fail inside production code (RED_STUB or RED_ASSERT).

### RED_MISSING

**Means:** The pull request changes production code, and no commit carries a `Lockflight-Red` trailer.

**Why:** The judge proves test-first work from red commits: without one there is nothing that shows the tests told the missing behaviour from the implemented one.

**Next:** Commit each story's new tests, with stubs for what does not exist yet, before the implementation, as a commit with the trailer `Lockflight-Red: 1` (2 for the next story); `redgreen red` makes that commit for you.

### TOO_MANY_RED_COMMITS

**Means:** The pull request has more red commits than `ci.maxRedCommits` (10 by default).

**Why:** Each red commit is built and run, so the limit keeps the judge within its time budget; a pull request this large is better split.

**Next:** Split the pull request so each has at most `ci.maxRedCommits` red commits, or raise that limit on the target branch.

### COMPILE_RED

**Means:** The tests do not compile at a red commit.

**Why:** A test that does not compile cannot fail in production code, so it shows nothing about the behaviour. The stubs belong in the red commit with the tests.

**Next:** Make the red commit compile: declare every member its tests use as a stub that throws `NotImplementedException`, in the same commit as the tests.

### RED_ADDS_NO_TESTS

**Means:** A commit carries a `Lockflight-Red` trailer and adds no test the fork point or an earlier red did not have.

**Why:** A red commit is the proof that new tests failed before the code; with no new test there is no proof.

**Next:** Put the story's new tests in the red commit, or remove the `Lockflight-Red` trailer from a commit that adds none.

### RED_WEAKENS_SUITE

**Means:** A test that existed before the red commit is deleted, newly skipped or made explicit there.

**Why:** A red commit adds tests. Switching others off in the same commit would hide what the change broke.

**Next:** Keep the existing tests in the red commit as they were: a red adds tests; it does not delete, skip or make explicit the ones before it.

### RED_OTHER

**Means:** A new test fails with another exception, or with a `NotImplementedException` thrown outside production code.

**Why:** Only a failure that comes from the missing behaviour shows the test is about it.

**Next:** The failure is not the stub's NotImplementedException thrown by production code; declare the member as a stub, or assert on the value it returns.

### RED_IN_TEST

**Means:** A new test fails in its own code or setup: a fixture, a constructor, `Assert.Fail`.

**Why:** A test that fails by itself says nothing about the production code.

**Next:** The test (or its fixture, constructor or Assert.Fail) fails by itself; make it call the production code and fail there.

### RED_NO_PRODUCTION

**Means:** A new test fails on an assertion without calling production code.

**Why:** A test that never reaches the code under change cannot be about it.

**Next:** The assertion fails without the test calling production code; call the member under test and assert on its result.

### GREEN_BEFORE

**Means:** A new test passes at the red commit, before the implementation exists (in at least one data row).

**Why:** A test that passes before the code exists does not tell the missing behaviour from the implemented one, which is what a red proves. Usually the implementation went into the red commit, or into a commit before it.

**Next:** Commit the tests before the code: the red commit holds the tests and stubs only, the implementation comes in later commits (reorder the branch if it went in first); a test of behaviour that already exists is no red, so make it describe what is still missing.

### SKIPPED

**Means:** A new test is skipped or did not run at the red commit (`Skip`, `Explicit`, `Ignore`, `Assert.Skip`, `Assert.Inconclusive`).

**Why:** A test that does not run proves nothing.

**Next:** The test is skipped or did not run; remove Skip, Explicit or Ignore (an Assert.Skip, Assert.Ignore or Assert.Inconclusive too).

### FLAKY

**Means:** Repeated runs of a test disagree: at a red commit, or a cycle test that passed at head only when run again.

**Why:** A verdict built on a test that changes its mind is not one; trace waits for a stable green too.

**Next:** Make the test deterministic (time, order, randomness, shared state), then push again; a cycle test must pass on its first run.

## Tests since red

The `tests.sinceRed` check: every cycle test at head is still the test that went red, or its change (an amend) was re-checked on the red commit's code.

### NO_ACCEPTED_CYCLE

**Means:** No red commit was accepted, so there is no cycle test to compare.

**Why:** Tests since red compares with the cycles the red check accepted; fixing red fixes this.

**Next:** Fix the red check first: tests since red compares with the cycles it accepted.

### AMEND_REJECTED

**Means:** A cycle test changed after its red, and the head's version passes (or fails for another reason) on the red commit's code.

**Why:** The changed test no longer checks what it went red for, so the implementation was never shown to be needed for it.

**Next:** Restore the test as it was at its red: it now passes on the red commit's code. If the test was wrong, a person amends it (`/lockflight:amend <Class.Method> --reason <why>` in Claude Code, or `redgreen amend` in a terminal) and `redgreen red` re-checks it, then push its new red.

### AMEND_WEAKENED

**Means:** A cycle test changed after its red and its head version has fewer rows (data rows, test cases) or fewer assertions than at red.

**Why:** An amend that drops part of what a test checked lowers the bar the implementation had to meet.

**Next:** Restore the rows and assertions the test had at its red; an amend may change what a test checks, not drop part of it.

### AMEND_UNCHECKABLE

**Means:** A cycle test changed after its red, and the head's test files cannot be built or run on the red commit's code.

**Why:** The amend cannot be re-checked, so the judge cannot vouch for it. A test file that existed at the red is laid over whole, so a later cycle's tests in it can break this build.

**Next:** Make the test side build on the red commit's code: amend a test before the next cycle starts, and keep each story's tests in new test files of their own.

### AMEND_CHANGED_EXPECTATION

**Means:** A cycle test changed after its red; the head's version still fails on the red commit's code, but the red commit's version fails on the head's code.

**Why:** The expectation changed after red. A stub fails any test, so failing on it proves little: a new red for that cycle has to prove the new expectation.

**Next:** The expectation changed after red: add a new red commit for that cycle (`Lockflight-Red: <cycle>`) whose tests fail with the new expectation, or restore the test as it was at red.

### CYCLE_TEST_MISSING

**Means:** A test that went red is gone at head: deleted, or renamed after its red.

**Why:** The proof of that cycle is gone with it. CI matches tests by name, so a rename counts as a loss.

**Next:** Keep the name each test went red with until the pull request merges: restore a deleted or renamed cycle test.

## Existing tests changed

The `tests.existingChanged` check (advisory): an existing test the pull request changed is run in its head version on the base's code.

### EXISTING_TEST_PASSES_ON_BASE

**Means:** An existing test the pull request changed passes on the base's code.

**Why:** It no longer tells the old behaviour from the new. A refactor that keeps the expectation reads the same, so a person decides.

**Next:** The changed test no longer tells the old behaviour from the new: the person who merges decides whether that is a refactor or a lost check (advisory).

### EXISTING_TEST_UNCHECKABLE

**Means:** An existing test the pull request changed cannot be built or run on the base's code.

**Why:** The change could not be re-checked, so only reading it shows what it does.

**Next:** The changed test's side does not build or run on the base's code; the person who merges reviews the change by reading it (advisory).

## Host tampering

The `tests.hostTampering` check: the test assemblies are scanned for the plain ways to forge a test result.

### HOST_TAMPERING

**Means:** New code in a test assembly could tamper with the test host: a module initializer, a process-exit handler, `Environment.Exit`, starting a process, a file write outside a temporary folder, reflection over a runner's types, setting CI variables.

**Why:** The tests run in the judge's job, so code like this could forge the results. Legitimate uses exist (a snapshot store that writes its files); accepting one is a person's decision, made on the target branch.

**Next:** Remove the listed code from the test assemblies, or a person accepts a legitimate finding by merging its baseline entry into `.lockflight/baselines/host-tampering.json` on the target branch first.

## Green at head

The `green` check: the whole suite runs at head and must pass, with every cycle test present.

### HEAD_DOES_NOT_BUILD

**Means:** The pull request's head does not build.

**Why:** Nothing can be judged on code that does not build.

**Next:** Make the pull request's head build: run `dotnet build` and fix the first error.

### TESTS_NOT_LISTED

**Means:** A tree's tests could not be listed: a project could not be evaluated, or a test application could not list its tests.

**Why:** The judge must know every test to say none was lost; an incomplete list could hide one.

**Next:** Make every test project listable: it must build, and NUnit or MSTest on Microsoft.Testing.Platform needs `Microsoft.Testing.Extensions.TrxReport` (VSTest: `Microsoft.NET.Test.Sdk`).

### HEAD_NOT_GREEN

**Means:** At head a test fails beyond the base's known-failing baseline, or a test was deleted, newly skipped or made explicit since the base, or the runner did not report every listed test.

**Why:** Green means the whole suite, not only the new tests: the change must not break or switch off what was there.

**Next:** Make the whole suite pass at head with nothing deleted, skipped or made explicit since the base; the detail names each problem.

### BASE_NOT_COMPARED

**Means:** The base could not be built or listed to compare with, or tests head skipped at run time could not be run on the base.

**Why:** Some verdicts are relative to the base (what was skipped, deleted or failing before). Without the base the judge cannot tell what the change did.

**Next:** The base could not be built, listed or run to compare with; fix the target branch's build, or re-run the check if a tool broke.

### NO_TESTS_RAN

**Means:** No test ran: none was found, all were skipped, or a filter matched none.

**Why:** A run that executed no test proves nothing, so it is never a pass.

**Next:** Make tests run: check that the test projects are found and that `test.stopFilter` in `lockflight.yaml` matches tests that are not skipped.

## Trace

The `trace` check: every requirement of the feature's spec is named by a test that went red, then green.

### GREEN_UNDECIDED

**Means:** Green at head was not decided, so trace does not know which tests pass there.

**Why:** Trace counts only tests that pass at head; it waits for green rather than blaming the tests.

**Next:** Fix green at head first: trace counts only tests that pass there.

### SPEC_UNRESOLVED

**Means:** The feature's spec could not be found or read: no feature named by the branch, a branch and a red commit naming different features, a missing `spec.md`, or a unit's feature not merged in the workspace shell yet.

**Why:** Trace needs the spec the pull request delivers, and a class that needs a spec is never let off for want of one.

**Next:** Name the branch after the feature (`specs/<id>/`, as Spec Kit does) and make sure its `spec.md` exists and is readable.

### SPEC_DUPLICATE_REQUIREMENT

**Means:** The spec defines a requirement id more than once, with a marker on one of its lines.

**Why:** With two definitions the judge cannot tell which one the marker applies to.

**Next:** Give each requirement one line in `spec.md`: an id with a marker must not be defined twice.

### SPEC_NO_REQUIREMENTS

**Means:** The spec defines no requirement (`FR-001 …`).

**Why:** There is nothing to trace the tests to.

**Next:** Write the feature's requirements in `spec.md` as `FR-001 …` lines (see the spec contract).

### SPEC_REQUIREMENT_UNKNOWN

**Means:** The tests name requirement ids the spec does not define (in a unit repository: the workspace shell's spec).

**Why:** A test traced to a requirement that does not exist covers nothing.

**Next:** Make the tests name requirement ids the spec defines, or add the requirement to the spec first.

### TRACE_UNCOVERED

**Means:** A requirement this pull request answers for is not named by a test that went red, then green.

**Why:** Every requirement must be shown by a test that failed before the code and passes with it.

**Next:** Name each listed requirement in a test that went red, then green (`[Trait("Requirement", "FR-001")]`, or the NUnit or MSTest property), in a red commit.

## Approvals

The `approvals` check reads the approval facts git can show: CODEOWNERS on the target branch, and spec-first.

### SPEC_NOT_ON_BASE

**Means:** Spec-first applies, and the feature's `spec.md` is not on the target branch (or, for a unit, in the workspace shell).

**Why:** Under spec-first a person approves the spec by merging it before the code; the code pull request then delivers an approved spec.

**Next:** Merge the feature's spec into the target branch in its own pull request first, then run this check again.

### SPEC_CHANGED_IN_PULL_REQUEST

**Means:** Spec-first applies, the spec is on the target branch, and this pull request changes it.

**Why:** A spec changed together with the code is no longer the spec a person approved first.

**Next:** Take the spec change out of this pull request and merge it on its own first: under spec-first the approved spec does not change with the code.

### CODEOWNERS_MISSING

**Means:** The target branch's CODEOWNERS does not give every spec path an owner.

**Why:** The platform's required code-owner review is the approval of record for a spec; without an owner nobody is required to review it.

**Next:** Give the spec paths an owner in CODEOWNERS on the target branch, and require code-owner review on the platform.

## Change class and policy

The `routing` and `policy` checks read the target branch's policy.

### ROUTING_RULE_BROKEN

**Means:** A routing rule of the target branch fails, such as a file moved out of a critical path.

**Why:** Routing sets what the pull request must prove; a move that lowers its class is refused.

**Next:** Undo what the routing rule forbids (the detail names it), or a person changes the routing policy on the target branch.

### POLICY_LOOSENED

**Means:** The pull request's `lockflight.yaml` is looser than the target branch's.

**Why:** The judge always uses the target branch's policy; a loosening shows here so the person who merges sees that it would apply to every later pull request.

**Next:** Tighten `lockflight.yaml` back to the target branch's rules; a looser policy is a person's decision, merged on its own.

### POLICY_NOT_COMPARED

**Means:** The pull request's `lockflight.yaml` could not be read to compare with the target branch's.

**Why:** Without the comparison the judge cannot say whether the policy is loosened.

**Next:** Make `lockflight.yaml` in the pull request readable so it can be compared with the target branch's.

## Review

The `review` check: the review of record, run in its own job.

### REVIEW_NOT_RUN

**Means:** No review ran for this head: no API key, a pull request from a fork, or the review job was skipped.

**Why:** Where the change's class requires a review, a pull request without one is not done.

**Next:** Give the CI review job its API key (a spend-limited key scoped to CI) and run the check again; a pull request from a fork is reviewed another way.

### REVIEW_STALE

**Means:** The review that ran is about another head.

**Why:** A review of other code says nothing about this head.

**Next:** Re-run the review job for the current head.

### REVIEW_FINDINGS

**Means:** The review did not pass: it has confirmed blocking findings, or findings it could not confirm.

**Why:** Findings are confirmed by running their reproductions; a confirmed blocker stands until it is fixed or a person dismisses it.

**Next:** Fix the review's confirmed findings (the review job's summary lists them) and push; a person dismisses a wrong one with `review dismiss`.

## The judge's run

Reasons that say the judge could not decide, not that the code is wrong.

### CI_BUDGET_EXHAUSTED

**Means:** The judge's time budget, `ci.timeoutMinutes` on the target branch, ran out before the check was decided.

**Why:** No build starts with three minutes or less left, so the evidence is always written; the checks not decided are NOT-EARNED.

**Next:** Raise `ci.timeoutMinutes` on the target branch (and the CI job's own time limit with it), or split the pull request.

### JUDGE_DID_NOT_FINISH

**Means:** The report job found no evidence from the judge: it was cancelled, killed at the platform's time limit, or failed to upload.
The judge also stops before writing evidence when the engine's lock check refuses the pinned engine; its annotations say why.

**Why:** Without evidence there is no verdict, so it is never a pass.

**Next:** Read the judge job's annotations first: when the engine's lock check refused the engine pin, re-running does not help, so fix what it names. Otherwise re-run the workflow: the judge job was cancelled, killed at the platform's time limit, or failed to upload its evidence.

### EVIDENCE_INVALID

**Means:** The judge's evidence or the review's result is about another head, does not have the expected shape, or disagrees with what the report works out from git.

**Why:** The report does not trust evidence it cannot match to the pull request.

**Next:** Re-run the workflow from the start; if it repeats, the job log shows what in the evidence disagreed (the code was not judged).

### NOT_JUDGED_YET

**Means:** The engine the project pins does not judge this check yet.

**Why:** A check the engine cannot decide is never a guess.

**Next:** Update the engine pin to a version that judges this check, or turn the check off in the target branch's policy.

### GATES_FAILED

**Means:** A gate step did not pass and the gates result names no reason (a result from an older engine).

**Why:** The step's own row says what failed.

**Next:** Open the gates result in the evidence: the first step that did not pass says what failed.

### TOOL_ERROR

**Means:** A tool Lockflight runs (git, `dotnet`, a test runner, gitleaks) failed, or gave output Lockflight could not read.

**Why:** ERROR says nothing about the code: the check could not run, so it cannot pass.

**Next:** A tool broke, not the code: re-run the check; if it repeats, read the job log for the tool's own error (the code was not judged).

## Gates

The steps of `lockflight gate run`, in the Stop hook and in CI's `gates` check.

### BUILD_FAILED

**Means:** The build failed.

**Why:** Nothing after the build can be judged.

**Next:** Fix the build error the detail names (`dotnet build` shows it in full).

### NEW_BUILD_WARNINGS

**Means:** The build has warnings its baseline (`.lockflight/baselines/build-diagnostics.json`) does not accept.

**Why:** Warnings are held at the level a person accepted, so new ones are fixed rather than piled up.

**Next:** Fix the new warnings; a person accepts today's warnings once with `baseline build`, merged in its own pull request.

### FORMAT_NEEDED

**Means:** Lines the change touched are not formatted as `dotnet format` would format them.

**Why:** Only the changed lines are judged, so existing files need not be reformatted.

**Next:** Format the changed lines the detail lists (`dotnet format` fixes them, but also rewrites the rest of each file).

### NEW_SUPPRESSIONS

**Means:** The change adds analyzer suppressions (such as `#pragma warning disable`, `[SuppressMessage]`, `<NoWarn>` or a severity lowered in `.editorconfig`; the [full list](configuration.md#new-suppressions-fail-the-pr-profile)) that the baseline does not accept.

**Why:** A suppression silences a check instead of passing it; accepting one is a person's decision, by file and rule.

**Next:** Fix what the new suppressions silence; a person accepts a needed one, by file and rule, with `baseline suppressions`.

### DOCS_REFERENCES_BROKEN

**Means:** The change breaks a reference in an instruction file (CLAUDE.md, AGENTS.md, `.claude/`): a link, an anchor, a path or a command line it names no longer exists ([references](configuration.md#references-the-change-breaks-in-the-instruction-files)).

**Why:** Agents follow those files; a stale reference sends them wrong.

**Next:** Fix each reference the change broke in the instruction files (CLAUDE.md, AGENTS.md, .claude/), or restore what it names.

### EF_MIGRATION_MISSING

**Means:** An EF Core model changed without a migration.

**Why:** The model and the database schema would drift apart.

**Next:** Add a migration for each changed model (`dotnet ef migrations add <name>`) and commit it with the change.

### EF_CHECK_FAILED

**Means:** `dotnet ef` could not check a model for pending changes.

**Why:** The check could not run, so it cannot pass.

**Next:** Make the model checkable: EF Core 8 or later, and a startup project that references `Microsoft.EntityFrameworkCore.Design`; or set `gates.ef.pendingModelChanges: off`.

### TESTS_FAILED

**Means:** A test failed, timed out or was aborted, beyond the known-failing baseline; or failures could not all be attributed to known ones.

**Why:** A baseline covers failures it can see, never results that are missing.

**Next:** Fix the failing tests the detail names; a failure that predates the change is accepted by a person with `baseline tests`, merged first.

### TEST_RUN_BROKEN

**Means:** The test run broke: no report, a run that did not complete, a failing exit code without a failed test, or an exit code that is not a test verdict.

**Why:** A run with missing results cannot vouch for the code.

**Next:** Run `dotnet test` to see why the run broke (a test host crashed, a project did not run, no report was written) and fix that.

### TEST_SETTINGS_INVALID

**Means:** A test setting in `lockflight.yaml` cannot run: a `test.stopFilter` Microsoft.Testing.Platform cannot express, or options a test application rejected (exit 5).

**Why:** The run did not start as configured.

**Next:** Fix the test settings in `lockflight.yaml` the detail names (`test.stopFilter`, `gates.test.framework`).

### COVERAGE_REPORT_MISSING

**Means:** Patch coverage is configured, and the test run wrote no coverage report (or a test application rejected the coverage options).

**Why:** Without a report the changed lines' coverage is unknown.

**Next:** Reference the coverage collector's package from every test project (`doctor` lists the ones missing it), or set `gates.coverage.collector: none`.

### PATCH_COVERAGE_LOW

**Means:** The tests execute fewer of the changed coverable lines than `gates.coverage.patchThreshold` asks.

**Why:** New code should be exercised by the tests that come with it.

**Next:** Cover the changed lines the detail lists with tests, up to `gates.coverage.patchThreshold`.

### VULNERABLE_PACKAGE

**Means:** A package has a new High or Critical advisory.

**Why:** Known-vulnerable dependencies are fixed before merge; Moderate and Low ones are reported only.

**Next:** Update each listed package to a version without the advisory.

### SECRET_FOUND

**Means:** gitleaks found a possible secret.

**Why:** A committed secret is exposed to everyone with access to the history; rotate it even after removing it.

**Next:** Remove the secret and rotate it; a person accepts a false positive in `.gitleaksignore`.

### SECRET_ALLOWED_INLINE

**Means:** Added lines silence gitleaks with an inline `gitleaks:allow` comment.

**Why:** Anyone could add that comment; a false positive is accepted in `.gitleaksignore`, which a person reviews.

**Next:** Remove the inline `gitleaks:allow` comments; a person accepts a false positive in `.gitleaksignore` instead.

### GITLEAKS_MISSING

**Means:** gitleaks is not installed, so secrets were not scanned.

**Why:** A scan that did not run is never a pass.

**Next:** Install gitleaks on `PATH`, or set `LOCKFLIGHT_GITLEAKS` to its path.

### GIT_HISTORY_MISSING

**Means:** git could not compare with the base: a shallow checkout, or a `--base` ref a repository does not have.

**Why:** The steps that judge the change need the diff against the base.

**Next:** Fetch the full history (`fetch-depth: 0` in the checkout) so `--base` is reachable in every repository.

### GIT_UNAVAILABLE

**Means:** The step needs a git repository, and git could not read it.

**Why:** Secrets, references and changed files are read from git.

**Next:** Run the gates in a git clone with git on `PATH`.

### STEP_TIMED_OUT

**Means:** A step's tool ran out of the gates' time limit (`gates.timeoutSeconds`; the Stop gate at most 540 s).

**Why:** A step that did not finish cannot vouch for the code.

**Next:** Make the step faster, or raise `gates.timeoutSeconds`; the Stop gate leaves slow tests to CI with `test.stopFilter`.

### ENVIRONMENT_PROBLEM

**Means:** The machine lacks something a tool needs: a .NET runtime, a native toolchain, network access for restore, Docker.

**Why:** ERROR: the code was not judged. The detail names the problem and its fix.

**Next:** Fix the machine as the detail says (a runtime, a toolchain, network for restore, Docker), then run the gates again.

### SCANNER_REPLACED

**Means:** The gitleaks binary the secrets step was about to run does not have the SHA-256 the CI template recorded when it
installed it (`LOCKFLIGHT_GITLEAKS_SHA256`): something replaced it after the install, most likely code the pull request's build ran.

**Why:** ERROR: a replaced scanner could report no secrets whatever the code holds, so the scan is not run at all.

**Next:** Do not re-run: something in the build replaced the secrets scanner after it was installed. A person reviews the pull request's build files (projects, props and targets, scripts) for what replaced it.

## Blocked reports

When a run stops because a person must act, it leaves a `lockflight.blocked/v1` report: in CI inside `verdict.json` (and in the
PR comment), locally in `.lockflight/local/blocked.json`. The report names who must do what, never a command; the Next line below
is derived from its reason code (see [`blocked validate`](commands.md)). The PR comment shows it, and so does the console of a
`redgreen` step refused for a missing or stale approval.

### approval-missing

**Means:** An artifact the work needs (the spec, or the tasks) has no approval.

**Why:** Approvals are a person's decision about intent; red/green does not start without them.

**Next:** A person reads the artifact and approves it: `/lockflight:approve` typed in Claude Code, or `approve spec` / `approve tasks` in a separate terminal.

### approval-stale

**Means:** An approved artifact changed after it was approved.

**Why:** An approval binds the files' hashes; changed content is not what the person approved.

**Next:** The approved file changed since: a person reads it again and approves it again (`/lockflight:approve`).

### spec-not-on-base

**Means:** Spec-first applies and the spec is not merged first (on the target branch, or in the workspace shell for a unit).

**Why:** See [SPEC_NOT_ON_BASE](#spec_not_on_base).

**Next:** Merge the feature's spec into the target branch in its own pull request first, then run this check again.

### codeowners-missing

**Means:** The target branch's CODEOWNERS does not cover the spec paths.

**Why:** See [CODEOWNERS_MISSING](#codeowners_missing).

**Next:** Give the spec paths an owner in CODEOWNERS on the target branch, and require code-owner review on the platform.

### class-above-limit

**Means:** The change's class is above what may proceed without a person.

**Why:** The class sets how much proof a change needs; above the limit a person decides.

**Next:** A person decides whether the change may go ahead at its class: they review it, or change the limit in the target branch's policy.

### protected-change-needed

**Means:** The work needs a protected file changed (the policy, CI, a baseline), which only a person does.

**Why:** Protected paths are what the agent cannot loosen for itself.

**Next:** A person makes the protected change the need names, or reviews and merges it; an agent cannot.

### baseline-needed

**Means:** A baseline (known-failing tests, build warnings, suppressions) must be accepted by a person.

**Why:** A baseline lowers a bar, so it is a person's decision, merged on its own.

**Next:** A person accepts the baseline (`/lockflight:baseline <kind>` typed in Claude Code, or `baseline <kind>` in a separate terminal) and merges it in its own pull request first.

### question

**Means:** A person must answer questions before the work goes on.

**Why:** The answer is a decision the agent must not make alone.

**Next:** A person answers the report's questions; the recommended option is listed first.

### budget-exhausted

**Means:** The run's budget ran out.

**Why:** The work stopped rather than spend beyond what a person allowed.

**Next:** A person raises the run's budget or narrows the work, then runs it again.

### time-exhausted

**Means:** The run's time ran out; in CI, the judge's `ci.timeoutMinutes`.

**Why:** See [CI_BUDGET_EXHAUSTED](#ci_budget_exhausted).

**Next:** Raise `ci.timeoutMinutes` on the target branch (and the CI job's own time limit with it), or split the pull request.

### config-invalid

**Means:** The configuration is invalid.

**Why:** Nothing can be judged under a policy that cannot be read.

**Next:** A maintainer fixes the configuration the need names; `doctor` says what is wrong.

### engine-untrusted

**Means:** The engine is not the one the project's engine lock pins.

**Why:** Only the pinned engine's verdicts count.

**Next:** A person installs the engine the project's engine lock pins (`~/.lockflight/bin/lockflight engine install` in a separate terminal); `doctor` explains the mismatch.

### access-needed

**Means:** Access the work needs is missing, such as the CI review job's API key.

**Why:** Granting access is a maintainer's decision.

**Next:** A maintainer grants the access the need names (for the CI review: the review job's API key), then the check runs again.

## Next

- [Troubleshooting](troubleshooting.md): fixes grouped by what you see
- [Commands](commands.md#ci): what `lockflight ci` and `ci report` check
- [CI hardening](ci-hardening.md#what-the-ci-judge-shows-and-what-it-does-not): what the judge shows, and what it does not
