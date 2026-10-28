# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). The plugin and the `Lockflight.Cli` engine share one version.

## [Unreleased]

## [0.2.0-alpha.7] — 2026-10-28

### What to do after upgrading
- **Update the engine in every project.** The plugin now needs engine 0.2.0-alpha.7 or later, because the in-session
  commands, the amend re-check and the linked engine's hash record are new in it. In a separate terminal window (not with
  `!` in Claude Code), run `lockflight update` from the project root and commit what it lists. It re-pins and re-links the
  engine, which also writes the hash record. Until then, Claude Code tells you at session start that the plugin needs a
  newer engine.
- **`lockflight update` also refreshes the Lockflight block in `CLAUDE.md`**, which now tells the agent never to try a
  human decision and how to propose a protected change. Until you run it, the block is reported as outdated (`adopt plan`
  lists it to be replaced). Commit the new block with the rest.
- **Run `lockflight init` again** in a project set up before this release, to add the new `Edit` deny rules and sandbox
  write denials to `.claude/settings.json`. Your own entries are kept; `--no-sandbox` leaves out the sandbox list. Run it
  again after adding protected files; `doctor` names any it misses.
- **Install from source takes the plugin from your checkout.** `scripts/dev-install.sh` (and the new
  `scripts/dev-install.ps1`) now point the `lockflight` marketplace at the checkout, so the plugin and the engine come from
  the same commit. To go back to the GitHub marketplace: `claude plugin marketplace add lockflight/lockflight`.
- **Scripts that read the status line:** a red Stop gate now reads `stop gate FAIL` (lower case), like `stop gate PASS`.

### Added
- **In-session `/lockflight:baseline`, `/lockflight:reset` and `/lockflight:amend`**, in two steps like
  `/lockflight:approve`: `/lockflight:baseline build|tests|deps [--path <unit>]`, `/lockflight:reset [--feature <id>]` and
  `/lockflight:amend <Class.Method> [--feature <id>] --reason <why>`. Step 1 shows you, not the agent, what would be recorded,
  with a one-time code. Step 2 records it only while that state is unchanged. The steps say `Step 1 of 2` and `Step 2 of 2`,
  the status line shows the line to type while a code waits, and the command typed alone repeats that line. `update` and
  `adopt` stay terminal-only.
- **`lockflight redgreen amend <test> --reason "…"`** (human-only) lets one locked test of the open red cycle change,
  instead of throwing the whole cycle away with `redgreen reset`. Only the file that declares the test becomes writable.
  The next `redgreen red` re-checks the amended tests on a snapshot of the tree as it was at red, so a corrected test must
  still fail even when the implementation already exists. `redgreen green` waits for that re-check. Who amended, when, why
  and the amend's diff are shown in `redgreen status`, `trace` and the pull request summary of `review status`.
- **Test files are checked between red and green.** `redgreen red` records a hash of every file in the locked test
  projects. `redgreen green` and `lockflight review` refuse while one of them was changed, added or deleted without a
  recorded amend, and name each file.
- **`redgreen red` explains each red class** in a line under each test (`--verbose` adds the exception, the deciding stack
  frame and the production member it found), and warns about a new test that contains no assertion.
- **`lockflight docs check`** lists references in `CLAUDE.md`, rules, skills, the Spec Kit constitution and `docs/` that no
  longer resolve: links to missing files or headings, paths that git history deleted or renamed, engine commands or options
  that do not exist, and plugin commands the repository does not have. It also names instruction files over Claude Code's
  size targets. `doctor` shows a short advisory summary of the same scan.
- **`lockflight rules audit`** lists every rule in your instruction files with what backs it: checked by a Lockflight
  guardrail, related to a check, or guidance only, with a suggestion where one fits. It changes nothing.
- **`lockflight doctor --canaries[=full]`**, a self-test: it plants a violation in front of every control in a temporary
  scratch project (never your project) and reports each as `caught`, `MISSED` or `BROKEN`. `=full` also builds a generated
  .NET project and runs the real test gate, red, amend and green on it.
- **A task is completed only after the stop gate passed.** With the stop gate on, the plugin's new `TaskCompleted` hook
  refuses to mark a task in Claude Code's task list completed until the stop gate has passed for the current changes. It
  never builds; `gate run --profile stop` earns the pass in the middle of a turn.
- **Claude Code's own settings back the guard.** `init` and `adopt apply` add an `Edit` deny rule for each protected path
  and list the protected paths as sandbox write denials in `.claude/settings.json`. The sandbox itself is not switched on
  for you: the guide shows how a developer or a team turns it on, and what that costs builds and tests. `doctor` has a new
  `sandbox` check.
- **The linked engine is pinned by its hash.** `link` records the engine's SHA-256 beside it. The hook shim and `doctor`
  refuse an engine replaced after linking, even in the same second as the last passed check: reads still pass, writes and
  in-session commands are blocked, and the message says how to link again.
- **Windows install and update.** `scripts/dev-install.ps1` installs from source on Windows, and `lockflight update` works
  there, including replacing the engine while it runs. Both installers put `~/.lockflight/bin` on `PATH` for new terminals,
  so `lockflight` works by name (`--no-modify-path` or `-NoModifyPath` skips that).
- **Review findings are checked against the spec and the change's tests.** A blocker or major finding may quote the spec;
  the run records whether the quote appears in it, for you to read (this changes no verdict). When a confirmed finding comes
  with a fix and that fix makes one of the change's own tests fail, the finding is *contested* (NOT-EARNED, naming the test)
  instead of FAIL.

### Changed
- Every hand-off to a person names the place first: the in-session command to type, or a separate terminal window (not
  `!` in Claude Code), with the command inside the sentence rather than on a line of its own.
- The session banner and the `CLAUDE.md` block tell the agent never to run a human decision, even when asked or just to
  try it, and how to change a protected file: write the new version outside the project, run `propose`, and give you the
  `git apply` command.
- `adopt plan` says when `CLAUDE.md` will be created or is empty, instead of "about 0 tokens".
- The Spec Kit preset carries the release version, like the plugins and the engine.
- `workspace sync` says when it rewrites a unit's edited CI workflow (it is generated policy).
- The documentation explains task roles and red classes in the feature skill, the checks a maintainer runs before a
  release, team policy through managed settings and platform notes; questions and ideas go to Discussions.

### Fixed
- **The test gate never passes a run that executed nothing.** A Microsoft.Testing.Platform summary that counts no test that
  ran is NOT-EARNED; a `test.stopFilter` that matches no test is NOT-EARNED and names the filter; when a filter leaves one
  test project empty and others ran tests, the run is judged by the tests that ran.
- **Red/green accepts real assertion failures.** Failures from Shouldly, FluentAssertions, AwesomeAssertions, NSubstitute,
  Moq and Verify, and xUnit v2 assertions over VSTest, now count as RED_ASSERT; before, they were rejected.
- Red/green runs projects on xunit.v3 3.x, shares one time limit across test batches, finds test projects in parallel
  within the time limit, and no longer lists a public class nested in a non-public one as an xUnit v2 test.
- Test runs started by the engine never open a snapshot diff tool on your screen.
- **Review:** reproductions are built in the test project nearest the cited code, with more context for the reviewer and
  one repair attempt for one that does not compile; the reproduction's test method runs, not a helper written before it;
  the reviewer's material is fenced as data, no customization is loaded from the working tree, and project guidance is
  taken from the merge base; two reviews of one project no longer share a build folder; a diff too large to show whole is
  no longer the review of record, which is said before the reviewer starts; a spec quote matches when it leaves out
  Markdown emphasis or code markers.
- **Guard:** it looks through `script`, `xcrun`, `watch` and GNU `parallel`, and blocks writes to the files Claude Code reads
  queued prompts from (scheduled tasks, loops, routines, mailboxes). On Windows, a path with `..` after a link or junction
  is judged the way Windows opens it, which closes a bypass.
- The ledger masks `--password x`, passwords in URLs, credential-named options and variables, and base64 secrets that
  contain `/` or `+`.
- `workspace sync` re-bases every protected glob a unit can match, so a unit's policy is never looser than the shell's.
- `doctor` runs the standard checks for a project that uses the Lockflight Spec Kit preset.
- `lockflight update` moves a project pinned to an older-scheme development build up to a newer prerelease.
- `redgreen reset` and `/lockflight:reset` delete the cycle's red-time snapshots.

## [0.2.0-alpha.6] — 2026-10-27

### Added
- `lockflight-crew`, an optional plugin in the same marketplace, with a test author agent: at the red step of
  `/lockflight:feature` it writes a story's failing tests against the declared stubs until `redgreen red` accepts them. The
  skill checks the recorded red itself and hands over to you after two failed rounds; without the plugin nothing changes.
- A benchmark of the test author against a plain agent on seeded stories (`tests/benchmark/crew/`), including stories
  built to provoke compile reds, test-side exceptions, a missing stub and requirement tagging across several classes.
- Review findings have a lifecycle: each gets a stable id, and a confirmed or unverified blocker/major finding stays open in
  every later `lockflight review` until its reproduction passes (fixed) or you close it with
  `lockflight review dismiss <id> --reason "…"` (human-only, records who, when and why). A later clean run no longer hides it.
- `lockflight review status` lists the review runs and the open, fixed and dismissed findings, with a summary for the pull
  request body (base, target, merge base, number of review runs, open findings).
- A `review:` section in `lockflight.yaml` (`model`, `maxUsd`, `target`). A review is the review of record only with those
  settings against a base whose commit is the merge base of HEAD and the target branch (default: `origin/HEAD`'s branch, else
  `main`); other runs are recorded with the reason and a clean one exits NOT-EARNED. The base now defaults to the target.
- Review evidence records the base and its merge base, the model and budget, the Claude Code version, tokens, the estimated
  cost and the wall time; the ledger gets `review.completed`, `finding.fixed` and `finding.dismissed` (no finding text), which
  the retro counts and the status line shows.

### Changed
- `/lockflight:feature` makes checking the red after the test author a numbered step: the very next action is
  `redgreen status`, quoted in the reply, because the agent's report is not evidence. A missing stub it reports is declared
  by the main session as a stub and the round repeated.
- The documentation has a home page organised by task, an "On this page" list on long guides, "Next" links at the end of
  each, and diagrams of the hook flow, guardrails and boundaries, and the workspace shell sample.
- `lockflight review` uses `opus` by default with a `3.00` budget;
  `--model` and `--budget` still override both.
- The guides now match the engine: the four hooks the plugin installs, every human-only command, red/green on xUnit v2
  as well as v3, what to do while the engine package is not published (install from source; no feed token), and uninstall
  steps for both install modes, the crew plugin and the marketplace.

- While the engine pin is a development build, `init` writes a CI workflow that runs by hand (`--ci pr` still available
  with a warning), and the workflow installs the engine outside `global.json`, so SDK 8 and 9 projects work.
- `doctor` warns when gitleaks is missing or Claude Code is older than 2.1.295; messages name `.lockflight/bin/lockflight`.
- `adopt plan` ends with the exact `adopt apply` command, including the Spec Kit preset path.
- The README and getting started describe the install that works today, the requirements (SDK-style .NET 8, 9, 10; macOS or
  Linux; Claude Code 2.1.295 or later; gitleaks) and a short path for monorepos and existing setups.
- The engine package carries a readme for nuget.org, an icon and third-party notices; the Spec Kit preset is pinned exactly.

### Fixed
- Gates no longer report PASS when they judged nothing or missed a failure: a Microsoft.Testing.Platform run in which every
  test is skipped is NOT-EARNED; a crashed, aborted or timed-out test host is ERROR or FAIL before the known-failing baseline
  is applied; failed test names are read from coloured output in GitHub Actions.
- In a workspace, the Stop gate and `gate run` judge a unit whose state changed since its last PASS, committed or not, with
  committed shared build inputs in each unit's key; `--path`, baselines and a Stop target inside a unit use the unit's own
  `build:`, framework and `stopFilter`. Coverage maps deterministic CI source paths (`/_/…`).
- The shell guard reads nested shell command strings in every spelling (`bash -ce`, `sh -c --`, here-documents, `eval`,
  positional parameters, `while read` loops); a command string it cannot see (`eval "$X"`, `curl … | sh`) is blocked. It
  judges paths where the operating system opens them (`..` after a symbolic link, Unicode composed names, Windows `\\?\`
  and UNC paths), binds `for` loop variables, treats hard links and `rsync --remove-source-files` as writes, and sees more
  writers in POSIX shells and PowerShell.
- A typed `/lockflight:approve` is never passed to the agent when the engine fails or is missing; a waiting code is
  discarded after five wrong codes; the digest describes exactly the bytes the code binds.
- `lockflight review` keeps an open finding's higher severity and re-runs every known reproduction before marking it fixed,
  replays runs in recorded order, and runs the reviewer with explicit permission and sign-in flags.
- `init` and `update` never lower the engine pin; `doctor` and `init` report an unreadable `.claude/settings.json` or tool
  manifest with the fix instead of failing.
- `adopt` installs a Spec Kit version that is not on PyPI from its GitHub tag and keeps a newer Spec Kit than the pinned
  one (installing only the preset); it no longer writes through symbolic links, and `adopt undo` skips unfinished backups.
- `link` copies only a native single-file engine, so platforms without their own package no longer end up with an engine
  that cannot start; the `any` package is gone and macOS Intel (`osx-x64`) has its own.
- The hook shim refuses an engine that is committed to the repository or owned by another user.
- `phase check tasks` no longer counts "Make tests pass…" as a test task; `trace` with no requirements is NOT-EARNED.
- `redgreen red` no longer accepts reds that never reach production code. An assertion counts as RED_ASSERT only when the test
  calls production code (else RED_NO_PRODUCTION); `Assert.Fail`, a throwing fixture or constructor and other test-framework
  failures are RED_IN_TEST; an exception other than the stub's, or one thrown by the runtime or a library, is RED_OTHER and
  is now rejected. Each rejected class is printed with what to change.
- `lockflight review` no longer passes when a blocker/major finding has a reproduction but the tests do not build: the
  finding is unverified (NOT-EARNED).
- The crew test author stops before `redgreen red` when a stub is missing, instead of getting a red of only part of the
  story accepted, which locked the test projects until a person reset them.
- The shell guard treats a drive-relative value (`C:dir`, what an unquoted `C:\dir` becomes in Git Bash) as unknown, so
  writes through it are judged as before.
- `adopt plan` reports hook scripts with the platform's own path separators.
- `init` adds `.claude/worktrees/` to `.gitignore`, so the checkouts Claude Code makes for isolated subagents are not shown
  as untracked files.

## [0.2.0-alpha.5] — 2026-10-15

### Added
- `lockflight update` installs the newest engine from a package feed when Lockflight was not installed from source
  (`--source <feed>` to override); it works whatever SDK a project's `global.json` pins.
- The plugin names the oldest engine it supports; session start and `doctor` report a project engine that is older.

### Changed
- Development builds are numbered after the last release (`0.2.0-alpha.4.dev.<stamp>`), so moving a project to the next
  release is an upgrade; `doctor` suggests `update` for a project still pinned to a development build.

### Fixed
- Text from the project (file names, test names, spec lines, ledger fields) can no longer send escape sequences to your
  terminal; control characters are shown visibly.
- `lockflight propose` refuses names with control characters, files over 8 MB, and a proposals directory that is a symbolic
  link, and handles file names starting with `-`.
- `/lockflight:approve` refuses a tampered or misplaced waiting-code file, a clock that went back, and a `--feature` given as
  a path, and refuses instead of passing the command to the agent when the engine fails.
- The session-start notice about a newer installed engine compared the platform folder name instead of the version.

## [0.2.0-alpha.4] — 2026-10-15

### Added
- Approve a spec or tasks without leaving Claude Code: type `/lockflight:approve spec|tasks`, review what the engine shows
  you, then type the line with its one-time code. It works only when typed in an interactive session, and the approval is
  bound to the content you were shown. The guard blocks any tool call that would queue the command as a prompt (a scheduled
  task, a loop, a message to a session). The ledger's `approval.recorded` line has a `channel` field (`terminal` or
  `session`).
- `lockflight propose <path> --from <file>` (or `--delete`) writes a change to a protected file as a git patch in
  `.lockflight/local/proposals/`, one per file. It prints the protecting rule, a high-risk flag, a diffstat and the
  `git apply` command for you to run, and applies nothing itself. The `CLAUDE.md` block and the guard's block message point
  the agent at it instead of hand-written patches.
- The ledger records what started each gate run (`command` or `stop-hook`), how long it took and whether the Stop cache
  answered it; `ledger retro` shows how many Stop gates ran, how many the cache answered and their total time.
- `doctor` checks that a development tool pin can be restored on this machine (local feed or tools cache).

### Changed
- `lockflight approve --sign` records nothing when signing fails, instead of leaving an unsigned record.
- The skills, the session banner and the `CLAUDE.md` block run the project's linked engine (`.lockflight/bin/lockflight`), so
  commands work when the tool pin is a development build that no package feed has. A stale development pin tells the agent to
  ask you for `lockflight update`.
- A human-only command refused inside Claude Code (also with `!`) says to use a separate terminal window and prints the exact
  line to copy.
- Approvals are recorded in the ledger as `approval.recorded`, with the feature, phase, artifact hash prefixes and signing
  status, and no longer include the approver's email.

### Fixed
- The shell guard follows a variable that the same command line sets to plain text (`S=/tmp/out; cd $S && …`), so writes to a
  scratch directory are no longer blocked. Variables it cannot know exactly are judged as before.
- `git archive -o FILE` is checked as a write to `FILE`.
- Per-unit Stop timings are no longer counted as incidents in `ledger retro` or shown as verdicts in the status line.
- `phase check tasks` names each misplaced task with its line and why it counted as implementation, and says how to mark test
  and stub tasks.
- `phase check tasks` recognises frontend and test-file tasks ("Spec for …", `*.spec.ts`, `*.test.tsx`, `*Tests.cs`, "Cover …
  with tests"), stubs worded "skeleton", "declare signatures" or `NotImplementedException`, and FR ids anywhere on a task line.

## [0.2.0-alpha.3] — 2026-10-15

The first public release. Earlier versions were private previews; this entry summarises what Lockflight does today.

### Added

**Gates**
- `gate run` with the `edit`, `stop` and `pr` profiles: format of the changed lines, build with no new warnings, tests (MTP and
  VSTest, xUnit v3 and v2), patch coverage on changed lines, vulnerable packages, secrets and protected changes.
- Three-valued verdicts (`PASS`, `FAIL`, `NOT-EARNED`) with a `Scope:` line and stable exit codes.
- Baselines for brownfield repositories: accepted build warnings, failing tests and advisories, recorded by a person.
- The Stop gate: build and tests before the agent's turn ends, cached by an inputs hash, naming the failing tests.

**Guard**
- PreToolUse guard for protected paths: edits, and writes from Bash and PowerShell commands, with path canonicalisation
  (symbolic links, Windows short names, alternate streams). Agent worktrees under `.claude/worktrees/` get the same rules.
- Recovery mode on a fresh clone and a tighten-only lockdown.

**Lifecycle (standard profile)**
- A Spec Kit preset: requirement IDs, test-first tasks, `[unit:…]` tags.
- Phase checks, hash-bound approvals (optionally SSH-signed and verified in CI), red/green evidence with a test lock, the
  requirement trace and the review of record with reproduction tests.

**Setup**
- `init` (lite and standard, `--ci pr|manual|none`), `doctor`, `link`, `adopt plan|apply|undo` for an existing Claude Code and
  Spec Kit setup, and `workspace sync` for workspace shells with service repositories beneath them.
- Install from source with `scripts/dev-install.sh`; `lockflight update` brings an install and the current project up to date.
- A generated CI workflow that runs the pinned engine with the base branch's policy.

**Everything else**
- A local ledger of blocks and verdicts, with `ledger retro` for recurring incidents.
- Levi, the pixel lock-keeper, in your terminal and, if you like, in Claude Code's status line (`lockflight statusline`). Only
  people see him.

[Unreleased]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.7...HEAD
[0.2.0-alpha.7]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.6...v0.2.0-alpha.7
[0.2.0-alpha.6]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.5...v0.2.0-alpha.6
[0.2.0-alpha.5]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.4...v0.2.0-alpha.5
[0.2.0-alpha.4]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.3...v0.2.0-alpha.4
[0.2.0-alpha.3]: https://github.com/lockflight/lockflight/releases/tag/v0.2.0-alpha.3
