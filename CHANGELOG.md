# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). The plugin and the `Lockflight.Cli` engine share one version.

## [Unreleased]

## [0.2.0-alpha.3] — 2026-10-16

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
