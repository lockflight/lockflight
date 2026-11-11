# The spec contract

A feature in the standard profile is three Markdown files under `specs/<id>/`: `spec.md`, `plan.md` (optional) and
`tasks.md`. Two deterministic checks judge them: `lockflight phase check specify` and `lockflight phase check tasks`. This
page says exactly what those checks read, so that you can write the files by hand, with the
[crew's spec writer and planner](skills.md#the-crew-optional), or with Spec Kit and the Lockflight preset. Spec Kit is
optional: the engine reads the files, not the tool that wrote them.

**On this page:** [The feature directory](#the-feature-directory) · [spec.md](#specmd) · [plan.md](#planmd) ·
[tasks.md](#tasksmd) · [A complete example](#a-complete-example) · [Writing it by hand](#writing-it-by-hand) ·
[What the checks do not judge](#what-the-checks-do-not-judge)

## The feature directory

- A feature is a directory directly under `specs/` at the project root (the directory that holds `lockflight.yaml`). Its name
  is the feature ID. Name it `NNN-slug` (`001-discount-codes`, `002-…`): the engine matches a feature branch such as
  `001-discount-codes` or `feat/001-discount-codes` to it by name or by its number.
- Every lifecycle command takes `--feature <id>`. Without it, the engine uses the feature Spec Kit names (when Spec Kit is
  set up), else the feature branch, else the only directory under `specs/`. With several features and none named, it stops
  and asks for `--feature`. The order is in [getting started](getting-started.md#a-repository-with-an-existing-claude-code-or-spec-kit-setup).
- The engine keeps the feature's records (approvals, red/green, reviews) in `.lockflight/features/<id>/`. You never write
  there.

## spec.md

`phase check specify` passes when all five of its items pass. Lines inside HTML comments (`<!-- … -->`) are ignored by every
item, so template guidance may stay in comments. A comment counts when it starts a line; text after its `-->` on the same line
is shown when the spec is rendered, so it is read too.

| Item | Passes when | Written as |
|---|---|---|
| `spec.requirements` | at least one requirement | a list item whose first bold text is the ID: `- **FR-001**: System MUST …` (`*` bullets work too; the colon is optional; three or more digits) |
| `spec.unique-ids` | no requirement ID appears twice | `FR-001`, `FR-002`, … each once |
| `spec.clarified` | no line holds `[NEEDS CLARIFICATION` | resolve each open question, then delete the marker |
| `spec.no-placeholders` | no template placeholder is left (see below) | the feature's own words |
| `spec.stories` | at least one user story, and every story has a scenario | a `### User Story N - Title` heading, then at least one line with **bold** `**Given**`, `**When**` and `**Then**`, in that order, on the same line |

**User stories.** The heading is `### User Story <number> - <title>`, at level three. The separator may be `-`, `–`, `—` or
`:`, and a trailing `(Priority: P1)` is allowed. A story runs until the next `##` or `###` heading, so its scenarios must sit
between its heading and the next heading of either level (a `####` subheading does not end it). The story number is what
`tasks.md` calls `[US1]`, `[US2]`, …

**Scenarios.** A scenario line holds `**Given**`, then `**When**`, then `**Then**`, all three in bold (case does not matter)
on one line. Plain `Given … When … Then …` does not count. Numbered lines (`1. **Given** …`) are the usual form.

**Requirements.** A requirement line must start with a bullet and the bold ID: `- **FR-001**: System MUST reject …`. An ID
written anywhere else (`FR-001` in a sentence, `**FR-001**` without a bullet) is not a requirement. Each requirement should be
testable: it is what the tests are traced to with `[Trait("Requirement", "FR-001")]` (NUnit: `[Property("Requirement", "FR-001")]`, MSTest: `[TestProperty("Requirement", "FR-001")]`).

**Requirements verified outside .NET.** While Lockflight runs only .NET tests, a requirement that only a test in another
language can verify (a UI rule covered by a Vitest or Jest test in the web client of a .NET + Angular project) carries a marker
between the bold ID and the colon:

```markdown
- **FR-006** [outside .NET: web/src/app/streak-card.spec.ts]: The streak card MUST show the current streak
- **FR-007** [outside .NET: web/src/app/streak-card.spec.ts, web/src/app/streak.spec.ts]: The card MUST hide a zero streak
```

`outside` is lowercase, the marker comes before the colon, and several paths are separated by commas. Each path is relative to
the project root (in CI, the repository root). A path that is absolute, has a `..` segment, lies under `specs/` (the spec names
its own requirements) or is a directory or a symbolic link never counts. What the marker does, and what it does not:

- **Trace** (local and in CI) shows the requirement as *excluded — verified outside .NET (path)*: neither covered nor failing,
  but only while every file it names exists (in CI: at the pull request's head) and contains the requirement ID as a whole
  word. A missing file, a file that does not name the ID, or a path that does not count leaves the requirement *not covered*,
  as without the marker. A .NET test that went red, then green and names the requirement still covers it as usual, and a
  marked requirement whose .NET test no longer counts (deleted, renamed, skipped) is not covered: the marker does not hide it.
  A requirement ID defined twice with a marker on one of its lines makes trace NOT-EARNED.
- **Every requirement excluded is N/A, never PASS.** Locally trace exits 5. In CI the trace check is then N/A and, when the
  pull request's class needs trace, a required check, so the judge's verdict is NOT-EARNED.
- **Lockflight runs none of those tests.** The check proves only that the pointer leads to a file in the project that
  contains the ID, which a comment would satisfy as well as a test title. Whether that file is a test, and whether the test
  verifies the requirement, is for the person approving the spec and for review. In CI, each file's evidence says whether
  the pull request added or changed it.
- **It is visible.** The approval digest lists each marked requirement with its text and files, so the person approving the
  spec sees what leaves trace. When the CI judge evaluates trace (the pull request changes production code), the comment's
  headline counts the excluded requirements, and a marker added to, changed on or removed from a requirement the base already
  had is called out above the checks. A pull request that changes only the spec shows the marker in its spec diff alone.
- **Tasks.** `phase check tasks` warns (`WARN tasks.outside-dotnet`, never a failure) when no task that cites the requirement
  names Vitest, Jest or one of its files.

Mark only a requirement that has no .NET side. A rule the API enforces stays a normal requirement with a .NET test, even when
the UI also shows it.

**Structural requirements.** A behaviour-preserving refactoring, or an architecture rule a change establishes, has
requirements that no red/green cycle can prove: their tests pass before the change (characterisation and snapshot tests) or
call no production code (architecture tests that read types or sources). Mark such a requirement `[structural]`:

```markdown
- **FR-007** [structural]: Order totals MUST stay as they are after the pricing code moves to the Pricing module
- **FR-008** [structural]: The Domain project MUST NOT reference the Web project
```

- **Structural tests.** A test whose `Requirement` traits name only structural requirements is a structural test. It is
  not part of a red/green cycle: `redgreen red` (and the CI judge at a red commit) leaves it out and says so, so it never
  turns a cycle GREEN_BEFORE.
- **Trace runs them.** A structural requirement is covered by a structural test that is new since the code before the
  change and passes at head *and* on that code, with the files that declare the structural tests and every non-C# file of the
  test projects (project files, snapshots, expected-value data) laid over it. Every row of a theory must pass there. Locally
  that code is the commit `redgreen start` recorded. In CI it is the pull request's fork point (merge base). Such a test shows
  as `Class.Method (structural)`. A test that existed before the change (another feature's among them) does not count.
- **A structural test that fails on the code before the change does not count.** That covers a failure on an assertion
  about what production code returns, a stub, another exception, or one failing row. Such a test shows *behaviour changed —
  not structural*, and it keeps the requirement uncovered even when another structural test passes.
  The one exception is a test that fails there on an assertion **without calling production code** (RED_NO_PRODUCTION) and
  passes at head. That is how an architecture test that reads types or sources looks, and it shows as
  `(structural: architecture rule the change establishes)`. A test that calls production code and fails on an assertion
  there (RED_ASSERT) never counts as an architecture rule: that is what a behaviour change looks like. The production-call scan is static, though: a test that reaches
  production code only through reflection, a type argument (`WebApplicationFactory<Program>`) or a process, or that only checks
  that a file or type exists, looks the same. Review each architecture rule the trace lists.
- **What it proves, and what it does not.** Passing on both sides shows only that the test's own assertions held there. A
  test that asserts little, or that behaves differently in the temporary worktree, passes on both. Whether a structural test
  pins the behaviour its requirement names is for the person approving the spec and for review.
- **Keep structural tests in files of their own**, using only APIs the older code has. Their files are built against it,
  and a file that also holds tests needing newer code does not build there, so its structural tests show as *could not be
  checked* and cover nothing (trace FAIL, also when the cause is the build environment).
- **Local and CI can differ.** The two compare against different commits, CI also requires each test's compiled code to be
  the head's there, and a unit repository of a workspace shell does not get the structural check in CI, so its structural
  tests are judged as ordinary new tests at a red commit there.
- **It is visible.** The approval digest lists structural requirements with their text, the CI comment's headline counts
  them, and a marker added to, changed on or removed from a requirement the base already had is called out, as for
  `[outside .NET: …]`.

Mark only what the change must keep or the rule it establishes. A behaviour change, even inside a refactoring, stays a
normal requirement with a test that goes red, then green.

**A structural-only change needs no red commit.** When **every** requirement of the feature's spec is `[structural]` (a
refactoring, or an architecture rule), the CI judge accepts the pull request without a `Lockflight-Red` commit, provided git
shows all of these (the report job works them out again from git before it trusts the judge's evidence):

- it is a pull request, its change class comes from risk routing (the target branch's routing policy, or the default preset
  when its `lockflight.yaml` has no `routing:` section), and the repository is no unit of a workspace shell;
- no commit carries a `Lockflight-Red` trailer (a pull request with one is judged by its red commits);
- **a person gates the spec:** it is added or changed in this pull request and the target branch's CODEOWNERS gives its path an
  owner under `approvals.specPaths`, or spec-first applies and the spec was merged on the target branch first and is unchanged;
- no routing signal carries behaviour a structural test does not pin: `config.changed`, `deps.changed`, `db.*`,
  `api.contractFile`, `api.publicApiFile`, `binary.changed`, `critical.policy`, `ci.workflowChanged`.

Then `red` and `tests.sinceRed` are N/A, and `trace` and `tests.existingChanged` are required, which the build must earn:

- **Every requirement of the spec** is covered by a new structural test that passes on the fork point and at head (or is an
  architecture rule), not only those the spec diff touches.
- **No new test that is not structural.** A new test that names no structural requirement pins new behaviour, so trace is
  NOT-EARNED (STRUCTURAL_CHANGE_NEW_TEST): that test belongs in a pull request with a red commit.
- **Every existing test the pull request changed keeps what it pinned.** The judge runs the base's version of each one (with the
  base's helpers and test inputs) on the head's code. It must pass there (EXISTING_TEST_OLD_VERSION_FAILS otherwise), so a test or
  shared assertion helper loosened to accept new behaviour does not help. A test whose old version does not build on the new code,
  as after a rename, is NOT-EARNED (EXISTING_TEST_OLD_VERSION_UNCHECKABLE): keep the old API as a thin shim for that pull request,
  or change the tests in a pull request of their own.
- Green, the gates and host tampering apply as for any pull request, and the class is never lowered.

The comment then opens with *Structural-only change — no red commit required*, names the spec and how it was reviewed, and
says whether the changed lines' coverage was measured (without a coverage collector it says they were not). A spec that is all
`[structural]` but misses a condition keeps RED_MISSING, and the check's detail names the condition.

What this shows, and what it does not: the structural tests held on the fork point and at head, every changed existing test's old
version passes on the new code, and no test pins new behaviour. It does **not** show that the structural tests pin what the change
could have broken: a test that asserts little passes on both sides. The markers are written by whoever writes the spec, an agent
included; the claim "this change is structural" rests on the person who reviews the spec, which the platform enforces only where it
requires code-owner review on the spec path ([guardrails and boundaries](guardrails-and-boundaries.md)). A refactor without a spec
needs a red commit as before.

**Placeholders.** The check rejects text left over from a template:

- an ALL-CAPS word of three or more characters in brackets that is not a Markdown link: `[FEATURE NAME]`, `[DATE]`,
  `[TODO]`. This also catches bracketed IDs and acronyms such as `[US1]`, `[FR-001]` or `[API]`: in `spec.md`, write them
  without brackets;
- a bracketed branch name such as `[###-feature-name]`;
- any bracket that holds `e.g.`;
- the stock template's bracketed prompts, such as `[Brief Title]`, `[initial state]`, `[action]`, `[expected outcome]`,
  `[specific capability …]`, `[Entity 1 …]`, `[Measurable metric …]`;
- an unexpanded `$ARGUMENTS`.

Markdown links (`[spec.md](spec.md)`) and checkboxes are fine. The check names the first placeholder of each line, so
fix a line and run it again.

**Class (optional).** A line `**Class**: <class>` in the header, before the first `##` heading (next to `**Status**:`),
asks for at least that change class for every pull request of the feature: `fix`, `behaviour`, `feature` or `critical`. It can
only raise the class [risk routing](risk-routing.md) computes from the diff, never lower it; a lower value is shown as ignored,
and a value that is no class is reported and ignored. No phase check reads it.

Other sections (an overview, edge cases, key entities, success criteria, assumptions) are free-form: the check does not read
them, and the review of record reads the whole spec.

## plan.md

`plan.md` is optional, and no check reads its content. When it exists, the tasks approval binds it with `tasks.md`, so an edit
to it after the approval makes that approval stale. Write in it what the implementer and the test author need and the spec
does not say: the approach, the files, and above all the **declared signatures**, the public members the stubs will declare
and the tests will call. The crew's test author reads it.

## tasks.md

`phase check tasks` passes when all of its items pass.

| Item | Passes when |
|---|---|
| `spec.approved` | `spec.md` has a valid approval: a person approved it, and it has not changed since |
| `tasks.exists` | `tasks.md` and `spec.md` exist (shown only when one is missing) |
| `tasks.cover-requirements` | every requirement of `spec.md` is cited by at least one task |
| `tasks.cite-requirements` | every story task cites at least one requirement, and no task cites an ID that `spec.md` does not define |
| `tasks.tests-first` | in every story, the story has a test task, and no implementation task comes before its last test task |
| `tasks.outside-dotnet` | a warning only, never a failure: every requirement marked `[outside .NET: …]` has a task that cites it and names Vitest, Jest or one of its files (shown only when the spec marks one) |
| `tasks.units` | every `[unit:<path>]` tag names a unit listed in `lockflight.yaml` (shown only when the project has units or a tag is used) |

**A task line** is a checkbox item with a task ID, then optional markers, then the description:

```text
- [ ] T012 [P] [US1] (FR-001, FR-003) Write tests for percentage codes in tests/Shop.Tests/ApplyCodeTests.cs
```

| Part | Rule |
|---|---|
| `- [ ]` | a checkbox; `- [x]` marks it done (the checks treat both alike, and ticking a task leaves the tasks approval valid: any other edit makes it stale) |
| `T012` | `T` and three or more digits |
| `[P]` | optional: may run in parallel with others |
| `[US1]` | the user story it belongs to. A task without it (setup, foundational, polish) is not a story task and needs no requirement |
| `(FR-001, FR-003)` | the requirements it serves. A requirement ID counts anywhere on the line; the parentheses after the markers are the convention |
| description | what the task does, with the file it changes |

Lines that are not task lines (headings, prose, notes) are ignored, and so are HTML comments.

**Roles.** Each story task is a test, a stub or an implementation, decided from its line alone:

1. **Test**, when it sits under a heading that starts with `### Tests` (until the next heading of any level), or its
   description starts with test wording: `Tests …`, `Write tests …`, `Add tests …`, `Unit tests …`, `Contract tests …`,
   `Integration tests …`, one qualifying word before them (`xUnit tests …`), `Spec for …`, `Cover … with tests`. "Make tests
   pass …", "Run tests …", "Implement tests …" and "Fix tests …" write no test and are not test tasks, and a path that starts
   with a tests folder (`Add tests/Shop.Tests/…`) is a file name, not test wording.
2. Else **stub**, when it says `stub`, `skeleton`, `NotImplementedException`, or "declare" or "scaffold" with "signature(s)".
3. Else **test** again, when it names a test file (`…Tests.cs`, `…Test.cs`, a `*.Tests/` project, `*.spec.ts`,
   `*.test.ts`), does not start with "Implement" or "Make", and does not sit under a heading that starts with
   `### Implementation` (Spec Kit's `### Implementation for User Story N`, until the next heading of level 3 or higher): there
   the heading decides, so a task that adds a test harness or fixture under `tests/` is implementation. A test task there
   needs test wording, or belongs under the story's `### Tests` heading.
4. Everything else is **implementation**.

**Tests first.** Within each story, every implementation task comes after the story's last test task. Stub tasks may sit
anywhere; put them first, since the tests call the stubs. A story with no test task fails. When the check fails, it names
each misplaced task, its line and why it got its role.

**Units.** In a monorepo or workspace (`units:` in `lockflight.yaml`), a task may name its unit with `[unit:services/orders]`;
red/green then builds only the named units (all .NET units when the feature names none). See
[configuration](configuration.md).

Approving the tasks (`/lockflight:approve tasks`) runs the same check and refuses while it fails, and so does approving the
spec with `phase check specify`.

## A complete example

These three files pass `phase check specify`, and every item of `phase check tasks` once the spec is approved. Copy them as a
starting point.

`specs/001-discount-codes/spec.md`:

````markdown
# Feature Specification: Discount codes

**Feature**: `001-discount-codes`
**Status**: Draft

## User Scenarios & Testing

### User Story 1 - Apply a percentage code (Priority: P1)

A shopper types a discount code at checkout and sees the reduced total.

**Acceptance Scenarios**:

1. **Given** a cart of 80.00 and the code `SAVE10` (10 %), **When** the code is applied, **Then** the total is 72.00
2. **Given** a cart of 19.99 and the code `SAVE10`, **When** the code is applied, **Then** the total is 17.99
3. **Given** a cart of 80.00 and the code `NOPE`, **When** the code is applied, **Then** the total stays 80.00 and the result says "Code not recognised"

### User Story 2 - Expired codes are refused (Priority: P2)

A code stops working after its last day.

**Acceptance Scenarios**:

1. **Given** the code `SPRING` whose last day was yesterday, **When** it is applied, **Then** the total is unchanged and the result says "Code expired"
2. **Given** the code `SPRING` whose last day is today, **When** it is applied, **Then** the discount is applied

### Edge Cases

- A code typed in lower case is the same code (`save10` is `SAVE10`).

## Requirements

### Functional Requirements

- **FR-001**: System MUST reduce the cart total by the code's percentage, rounded to the cent, half away from zero
- **FR-002**: System MUST leave the total unchanged and report "Code not recognised" for an unknown code
- **FR-003**: System MUST match codes without regard to letter case
- **FR-004**: System MUST refuse a code after its last day (in UTC) and report "Code expired"

## Success Criteria

- **SC-001**: Every scenario above is covered by a test that failed before the code existed.
````

`specs/001-discount-codes/plan.md` (optional):

````markdown
# Implementation Plan: Discount codes

**Spec**: [spec.md](spec.md)

## Approach

`Checkout.ApplyCode` looks the code up in an in-memory `DiscountCatalogue` and returns the new total with a message. The
current date comes from an injected `TimeProvider`, so the expiry rule is testable.

## Declared signatures

```csharp
public sealed record DiscountResult(decimal Total, string? Message);

public sealed class Checkout(DiscountCatalogue catalogue, TimeProvider time)
{
    public DiscountResult ApplyCode(decimal cartTotal, string code);
}
```

## Files

- `src/Shop/Checkout.cs`, `src/Shop/DiscountCatalogue.cs`, `src/Shop/DiscountResult.cs`
- `tests/Shop.Tests/ApplyCodeTests.cs`, `tests/Shop.Tests/ExpiryTests.cs`
````

`specs/001-discount-codes/tasks.md`:

```markdown
# Tasks: Discount codes

**Input**: `specs/001-discount-codes/spec.md` and `plan.md`

## Phase 1: Setup

- [ ] T001 (FR-001, FR-003) Add DiscountCatalogue with the codes SAVE10 and SPRING in src/Shop/DiscountCatalogue.cs

## Phase 3: User Story 1 - Apply a percentage code (Priority: P1)

- [ ] T010 [US1] (FR-001, FR-002, FR-003) Declare the Checkout.ApplyCode stub and DiscountResult in src/Shop/Checkout.cs

### Tests for User Story 1

- [ ] T011 [P] [US1] (FR-001, FR-003) Write tests for percentage codes and letter case in tests/Shop.Tests/ApplyCodeTests.cs
- [ ] T012 [P] [US1] (FR-002) Write tests for an unknown code in tests/Shop.Tests/ApplyCodeTests.cs

### Implementation for User Story 1

- [ ] T013 [US1] (FR-001, FR-002, FR-003) Implement Checkout.ApplyCode in src/Shop/Checkout.cs

## Phase 4: User Story 2 - Expired codes are refused (Priority: P2)

### Tests for User Story 2

- [ ] T020 [US2] (FR-004) Write tests for the last day and the day after in tests/Shop.Tests/ExpiryTests.cs

### Implementation for User Story 2

- [ ] T021 [US2] (FR-004) Implement the expiry rule in Checkout.ApplyCode using the injected TimeProvider

## Phase 5: Polish

- [ ] T030 Update the README section on discount codes
```

The checks' output on these files, before the spec is approved:

```text
$ .lockflight/bin/lockflight phase check specify --feature 001-discount-codes
Feature: 001-discount-codes · phase: specify
PASS spec.requirements: 4 requirements
PASS spec.unique-ids: requirement IDs are unique
PASS spec.clarified: no open [NEEDS CLARIFICATION]
PASS spec.no-placeholders: no Spec Kit template placeholders left
PASS spec.stories: 2 user stories with Given/When/Then scenarios

$ .lockflight/bin/lockflight phase check tasks --feature 001-discount-codes
Feature: 001-discount-codes · phase: tasks
FAIL spec.approved: spec is not approved; ask the user to review it, then to type /lockflight:approve spec in Claude Code, …
PASS tasks.cover-requirements: all 4 requirements have tasks
PASS tasks.cite-requirements: every story task cites known requirements
PASS tasks.tests-first: in every story the tests come before the implementation
```

The one FAIL is the human gate: once you approve the spec, `phase check tasks` passes.

## Writing it by hand

1. Pick the next number and a short slug, and create `specs/<NNN-slug>/spec.md`. Work on a branch of the same name, or pass
   `--feature <NNN-slug>` to every command.
2. Write one `### User Story N - Title` section per slice of value a user would notice, each with numbered
   `**Given** … **When** … **Then** …` lines that name concrete values. Write the requirements as `- **FR-001**: System MUST …`
   lines under `## Requirements`. Mark what you do not know yet with `[NEEDS CLARIFICATION: …]`, and resolve it before you
   approve.
3. Run `.lockflight/bin/lockflight phase check specify` and fix each FAIL. Then approve the spec yourself:
   `/lockflight:approve spec` in Claude Code, or `.lockflight/bin/lockflight approve spec` in a terminal outside the session.
4. Optionally write `plan.md` with the approach, the files and the declared signatures.
5. Write `tasks.md`: per story, the stub task first, then the test tasks under `### Tests for User Story N`, then the
   implementation tasks; every story task cites its `(FR-…)`. Run `.lockflight/bin/lockflight phase check tasks` until it
   passes, and approve the tasks (`/lockflight:approve tasks`).

After that the lifecycle goes on as in [getting started](getting-started.md#standard-profile): `redgreen start`, then one
red/green cycle per story.

## What the checks do not judge

The checks are deterministic and read only the structure above. They do not judge whether the requirements are right,
whether a scenario matches its requirement, or whether the tasks are a good plan: that is your review before you approve, and
the review of record later reads the spec in full. A spec that passes is well-formed, not correct.

## Next

- [Getting started](getting-started.md#standard-profile): the lifecycle step by step
- [Plugin skills](skills.md): the feature skill and the crew's spec writer and planner
- [Commands](commands.md): `phase check`, `approve` and the rest
