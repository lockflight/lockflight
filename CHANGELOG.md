# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). The plugin and the `Lockflight.Cli` engine share one version.

## [Unreleased]

### Added
- `lockflight propose <path> --from <file>` (or `--delete`) writes a change to a protected file as a git patch in
  `.lockflight/local/proposals/`, one per file. It prints the protecting rule, a high-risk flag, a diffstat and the
  `git apply` command for you to run, and applies nothing itself. The `CLAUDE.md` block and the guard's block message point
  the agent at it instead of hand-written patches.
- The ledger records what started each gate run (`command` or `stop-hook`), how long it took and whether the Stop cache
  answered it; `ledger retro` shows how many Stop gates ran, how many the cache answered and their total time.
- `doctor` checks that a development tool pin can be restored on this machine (local feed or tools cache).

### Changed
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

[Unreleased]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.3...HEAD
[0.2.0-alpha.3]: https://github.com/lockflight/lockflight/releases/tag/v0.2.0-alpha.3
