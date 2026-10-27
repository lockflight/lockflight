# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). The plugin and the `Lockflight.Cli` engine share one version.

## [Unreleased]

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
- `lockflight review` uses `opus` by default (it leads public coding benchmarks among Claude models) with a `3.00` budget;
  `--model` and `--budget` still override both.
- The guides now match the engine: the four hooks the plugin installs, every human-only command, red/green on xUnit v2
  as well as v3, what to do while the engine package is not published (install from source; no feed token), and uninstall
  steps for both install modes, the crew plugin and the marketplace.

### Fixed
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
- The guard judges a path where the operating system opens it: a `..` after a symbolic link leads to the parent of the
  link's target (`ln -s .lockflight/local l` then `echo x > l/../../lockflight.yaml` is blocked), names are compared in
  Unicode composed form on both sides (a decomposed `café` matches its rule), and on Windows `\\?\`, `\\.\`, `\\?\UNC\` and
  local administrative-share paths map to their drive, while volume, device and other share paths it cannot map are blocked.

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

[Unreleased]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.5...HEAD
[0.2.0-alpha.5]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.4...v0.2.0-alpha.5
[0.2.0-alpha.4]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.3...v0.2.0-alpha.4
[0.2.0-alpha.3]: https://github.com/lockflight/lockflight/releases/tag/v0.2.0-alpha.3
