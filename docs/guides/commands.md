# Commands

Every `lockflight` command, its options and its exit codes: a reference for the person running Lockflight and for anyone
reading what the agent ran.

**On this page:** [Running the engine](#running-the-engine) · [Setup](#setup) · [Gates](#gates) ·
[Lifecycle](#lifecycle-standard-profile) · [Human decisions](#human-decisions) · [CI](#ci) · [Exit codes](#exit-codes)

## Running the engine

In a project that is set up, run every command as `.lockflight/bin/lockflight <command>` from the project root (the directory
holding `lockflight.yaml`). That is the engine the hooks run: `init`, `link` and `update` keep it at the pinned version, it works
whatever way Lockflight was installed, and it needs no package feed (on Windows, Git Bash finds `lockflight.exe` by this name).
The skills tell the agent to run it the same way. Before a project is linked, use `~/.lockflight/bin/lockflight` (installed from
source) or `dotnet lockflight` (the tool pin, also what CI and teammates who restore the pin use). `lockflight --help` prints
the same list.

Commands that take `--feature <id>` resolve the feature in this order:

1. the explicit `--feature`;
2. Spec Kit's `.specify/feature.json` (a malformed file is an error, not a fallback);
3. the only feature, when there is exactly one.

This resolution does **not** apply to `approve verify`: it verifies exactly the feature named with `--feature`, or every feature
the change since `--base` touches, never a guessed one.

Each command accepts only its own options. An unknown option (`gate run --profil pr`), a stray argument, a repeated option or an
option without its value is a usage error (exit 4); a value starting with `-` (for example `--base -x`) counts as missing.

Everything the engine prints, on standard output and standard error and in hook messages, is made safe for a terminal: file
names, test names, spec text, ledger fields and branch names come from the project, so a control character or escape sequence in
them is printed visibly (`^[` for ESC, `<U+202E>` for a bidirectional override) instead of acting on your terminal. Line feeds
and tabs stay; the status line and Levi keep their own colours.

## Setup

| Command | What it does |
|---|---|
| `init [--profile lite\|standard] [--ci pr\|manual\|none] [--no-link]` | Onboards the repository: `lockflight.yaml`, tool pin (in the existing `dotnet-tools.json`, else `.config/dotnet-tools.json`, else a new root one), `.gitignore`, `.claude/settings.json`, the CI workflow (installing every .NET channel the test projects target) and, for standard, `.gitattributes`. `--ci` sets when the workflow runs: `pr` (the default) on every pull request and push to `main`, `manual` only when started by hand (`workflow_dispatch`), `none` writes no workflow. Then it links the engine. Idempotent; it only merges into existing files. At a workspace shell root, a new manifest lists the child directories that look like units (their own repository, a solution, a `package.json`), and the child repositories are added to `.gitignore`. A root that builds by itself (a solution or project file at the root) is one repository: only child directories that are their own repository become units. |
| `doctor` | Readiness report: manifest, tool pin, whether the pinned engine can be restored (a development build from `scripts/dev-install.sh` is on no package feed: doctor looks for it in the local feed `~/.lockflight/feed` and the NuGet tools cache, without the network), a project still pinned to a development build once a release at or above it is installed (**development pin**: `update` moves it to the release), linked engine, whether the installed plugin works with the pinned engine (**plugin engine**: the plugin's `min-engine-version`, found through Claude Code's record of installed plugins), `.gitignore`, Claude settings, Git Bash (Windows), PyYAML for Spec Kit, the Lockflight Spec Kit preset (a FAIL in the standard profile), the `claude` CLI, and — when `gates.coverage.collector` is set — the collector package in every test project. In a workspace it also checks:
<ul>
<li>that every unit is cloned;</li>
<li>that no unit has its own <code>lockflight.yaml</code>;</li>
<li>that child repositories are ignored by the shell;</li>
<li>that the plugin is enabled at user scope.</li>
</ul>
Any project gets a warning about a <code>global.json</code> that pins an SDK older than 10. Each problem comes with its fix. |
| `statusline` | Claude Code's status line command: reads the session JSON on stdin and, in a Lockflight project, prints Levi's face and the last verdict from the local ledger (a guard block, a red Stop gate or a gate run in the past day). Plain text under `NO_COLOR`; nothing outside a Lockflight project. See [Levi and the status line](getting-started.md#levi-and-the-status-line). |
| `adopt plan` | Reads an existing Claude Code and Spec Kit setup and writes `.lockflight/adopt/plan.md`. It covers the hooks (the project's, each unit's and your user-level ones, read only), skills, agents, commands, Spec Kit, existing features, `CLAUDE.md` and directories that look like units, and gives each one an action: keep, protect, flag, add, upgrade or mark. Nothing is changed. |
| `workspace sync [--check]` | In a workspace shell, writes into every .NET unit that is its own repository the files its CI needs: `lockflight.unit.yaml` (the shell's policy re-based to the unit), the engine pin, the unit's baselines and the CI workflow (when missing). `--check` changes nothing and exits 1 when a unit is behind the shell. See [CI hardening](ci-hardening.md#workspace-shells-unit-repositories). |
| `link` | Copies the pinned engine to `.lockflight/bin/`, where the hook shim runs it in about 5 ms instead of `dotnet tool run`. |
| `version` | Prints the engine version. |

## Gates

| Command | What it does |
|---|---|
| `gate run [--profile edit\|stop\|pr] [--path <dir>] [--unit <path>[,…]] [--base <ref>] [--result <file>]` | Runs a profile (default `stop`) on the repository or `--path`. At the root of a workspace with units, it covers every changed unit (`pr`: every unit), or the `--unit` ones. `--base` sets the comparison point for format, coverage, secrets and protected changes (the branch's changes since the merge base count as changed). `--result` writes the JSON result envelope. The stop profile's budget is capped at 540 s, below the plugin's Stop hook timeout. |
| `hook <event>` | Called by the plugin. Reads Claude Code's hook JSON on stdin and answers with an exit code or JSON. The plugin registers `session-start`, `pre-tool-use`, `user-prompt-expansion` and `stop`. The engine also accepts `post-tool-use` and `subagent-stop` (logged only); an event it does not know is blocked with a message to update the engine. |

Profiles and steps are described in the [configuration reference](configuration.md#gate-profiles).

## Lifecycle (standard profile)

| Command | What it does |
|---|---|
| `phase check specify\|tasks [--feature <id>]` | Phase-exit checks. **specify:** requirement IDs, uniqueness, clarifications, no Spec Kit template placeholders left, stories. **tasks:** spec approved, every requirement covered, every story task cites one (an `FR-xxx` anywhere on the line), tests first. A task is a test task under a `### Tests for …` heading, when it starts with test wording ("Write tests …", "Spec for …", "Cover … with tests") or when it names a test file (`*.spec.ts`, `*.test.tsx`, `*Tests.cs`, a `*.Tests/` project) and does not start with "Implement" or "Make". A stub task ("stub", "skeleton", "declare/scaffold signatures", `NotImplementedException`) may sit anywhere. A failure names each misplaced task, its line and why it counted as implementation. |
| `redgreen start [--feature <id>]` | Records the write-once baseline of existing tests. It needs a valid tasks approval. It runs the whole suite once (on a large suite this takes minutes) and warns about tests that already fail (a person accepts them with `baseline tests`, merged in its own pull request first). |
| `redgreen red [--feature <id>]` | Refused while the spec or tasks approval is stale (as is `green`). Builds, finds the new tests, runs them and classifies each failure. A cycle is accepted only when every new test fails in production code (RED_STUB or RED_ASSERT). It is refused when a known test was skipped or marked `Explicit` since the baseline. New tests that pass are re-run to tell FLAKY from GREEN_BEFORE: at most four batched re-runs, all inside the red's `gates.timeoutSeconds` budget. If the budget runs out, the remaining tests stay GREEN_BEFORE and the red is still rejected, because a test that passes before the code exists rejects the red either way. |
| `redgreen green [--feature <id>]` | The cycle's tests pass (they run first; if any still fails, green stops without running the suite), nothing else broke, and no tests were deleted, newly skipped or newly marked `Explicit`. Explicit tests are read from the runner's listing, because a normal run reports them as not run. The full run must also be complete: a runner that crashed or timed out, or a report missing known tests, rejects the green. |
| `redgreen status [--feature <id>]` | Shows the cycles and whether tests are locked. |
| `trace [--feature <id>]` | A Markdown matrix of each requirement and the tests that went red, then green, for it. It builds and lists the current tests (it runs none). A test counts only while it still exists, still names the requirement, and is neither skipped nor explicit. A requirement whose last such test is gone fails the trace. If the build fails, the trace is NOT-EARNED. A stale spec or tasks approval prints a warning line. |
| `review [--feature <id>] [--base <ref>] [--model <name>] [--budget <usd>]` | Review of record (defaults `main`, `opus`, `3.00`: the review uses the strongest coding model; pass `--model` and `--budget` to change them). Exit codes: 1 when a finding is confirmed, 5 when a blocker or major finding is unverified. |
| `ledger retro [--days 30] [--min 2]` | Recurring incidents from the local ledger, each with a suggested remedy. |
| `propose <path> (--from <file> \| --delete) [<path> …]` | Proposes a change the agent may not make itself, such as an edit to a protected file. `<path>` is a project file (existing or new, protected or not); `--from` names a file outside the protected paths (typically the agent's scratch directory) that holds its complete new content, and `--delete` proposes removing it. Several pairs may be given; each file gets its own git patch in `.lockflight/local/proposals/<yyyyMMdd-HHmmss>-<name>.patch` (git-ignored), with `a/` and `b/` paths relative to the project root and line endings kept. For each it prints whether the file is protected and by which rule, a **high risk** flag for build configuration (`*.props`, `*.targets`, project files), analyzer and code-style policy, Claude Code hooks and settings, CI workflows, NuGet configuration, `global.json` and Lockflight's own policy, a diffstat, and the command for you: `git apply --check <patch> && git apply <patch>`, run at the project root. It applies nothing. Symbolic links are resolved first, so the patch names the real file (and its rule), and on a case-insensitive file system the stored spelling. It refuses (exit 4, no patch written) a path outside the project or inside `.git`, a name with a control character, a `--from` file inside a protected path (also through a link), binary content, a file larger than 8 MB on either side, content identical to the file, and a proposals directory that is a symbolic link or a file. If one pair is refused, no patch is written for any. The ledger records `proposal.created` with the path and rule, never the content. The agent runs it; the guard still blocks the agent's own `git apply`. |

A red/green step whose tools cannot build-and-list, list or run the tests ends ERROR (exit 3): that says nothing about the
tests. A build that fails is a verdict (FAIL, or COMPILE_RED for a red).

## Human decisions

> [!IMPORTANT]
> These refuse to run inside a Claude Code session (exit 2), and that includes a `!` command typed in Claude Code. Run them
> in a separate terminal window.

A `!` command runs inside the session and is refused. The refusal prints the exact line to copy, for example
`cd '/path/to/project' && .lockflight/bin/lockflight approve spec`. A spec or tasks approval can also be given in the session
by typing `/lockflight:approve` (below).

| Command | What it does |
|---|---|
| `approve spec\|tasks [--feature <id>] [--sign [--key <file>]]` | Records an approval bound to the artifacts' hashes. `--sign` adds an SSH signature (`ssh-keygen -Y sign`); the key defaults to `git config user.signingkey`. A signed approval is signed before it replaces the record: when signing fails, nothing is recorded (exit 3) and an existing approval stays as it was. |
| `/lockflight:approve spec\|tasks [--feature <id>] [--sign [--key <file>]]`, typed in Claude Code | The same approval without leaving the session. It works only when **you type it** in an interactive session (not for the agent's own skill call, a headless `claude -p` session or a scheduled turn). The first time, the engine shows you, and not the agent, what you are approving: each file with the start of its hash, the requirements and stories (spec) or the task counts and covered requirements (tasks), open `[NEEDS CLARIFICATION]` markers, the current approval, and a one-time code. Type `/lockflight:approve spec <code>` within 10 minutes to record it; the code works once, and only while the files stay exactly as shown. Typing it again shows a new code and the earlier one stops working. `--feature` takes a feature ID (a directory name under `specs/`), not a path. Nothing is recorded when anything is off: a wrong, used or expired code (also after the clock went back), a changed file, unknown options, or a waiting-code file under `.lockflight/local/session-approvals/` that is unreadable, written for another phase or replaced by a link. The record, the signature and the ledger line are the same as the command's (the ledger's `channel` says `session`). With `--sign`, the key must be in an SSH agent or be a security key: the hook has no terminal to ask for a passphrase. |
| `redgreen reset [--feature <id>]` | Discards the feature's red/green evidence (and says so when there is none). |
| `baseline build\|tests\|deps [--path <dir>]` | Accepts the current build warnings, failing tests or vulnerable-package advisories (brownfield adoption). In a workspace, `--path <unit>` writes that unit's own baseline. |
| `adopt apply` | Applies the reviewed plan. It refuses when the setup changed after the plan was made, or when the working tree is not clean. It backs up `CLAUDE.md`, `lockflight.yaml`, `.claude/` (not `settings.local.json`), `.specify/` and `specs/`, and adds a git ref `refs/lockflight/backup/<id>`. Then it:
<ul>
<li>adds the guard hooks' paths to <code>protected:</code>, the units, and a Lockflight block in <code>CLAUDE.md</code>;</li>
<li>marks the existing features as predating Lockflight (<code>.lockflight/baselines/adopted-features.json</code>): <code>approve verify --base</code> and <code>trace</code> accept them without approvals or red/green evidence until they get Lockflight records;</li>
<li>upgrades Spec Kit to 1.1.3 and installs the preset given by <code>--speckit-preset &lt;dir&gt;</code> (needs <code>uvx</code>; <code>--skip-speckit</code> leaves Spec Kit alone).</li>
</ul>
Files the team customised (the constitution, an edited template or skill) are kept and listed. Hooks, skills and agents are never removed. |
| `update [--source <feed>]` | Brings Lockflight up to date in one step. An install from source (`scripts/dev-install.sh`, which records the checkout in `~/.lockflight/source`) pulls that checkout and rebuilds and reinstalls the engine and the plugin. An install from a package installs the newest `Lockflight.Cli` from the NuGet sources (nuget.org by default; `--source` replaces them with a feed URL or a folder of packages) into `~/.lockflight/tools/<version>`, running the SDK from `~/.lockflight` so no project's `global.json` gets in the way, and points `~/.lockflight/bin/lockflight` at it; a prerelease install follows prereleases, and an installed engine newer than the feed's is kept. Then, in the current project, the new engine re-pins and links itself and refreshes an older Lockflight block in `CLAUDE.md`. Commit what it lists. At session start Claude Code shows you (not the agent) when a newer engine is installed, or when the plugin needs a newer engine than the project pins. Updating the engine changes the judge, so it is yours to run. |
| `adopt undo [--backup <id>] [--force]` | Reverts what apply changed: files apply changed or added go back, files it deleted come back. A file changed after apply (your later work) is kept and listed (exit 1); `--force` reverts it too. Files apply did not touch are never touched, and nothing behind a symbolic link or under `.claude/worktrees/` is archived or written. If apply fails part-way, it rolls back to the backup by itself. |

## CI

| Command | What it does |
|---|---|
| `gate run --profile pr --base <sha>` | What the generated workflow runs. |
| `approve verify (--feature <id> \| --base <ref>) [--allowed-signers <file>] [--require spec,tasks]` | Exactly one of `--feature` or `--base` is required. Checks approval records (which must name their own feature and phase), artifact hashes and, with `--allowed-signers`, the signatures. `--base` covers every feature the change touches, renamed or deleted records included; a feature whose directory is gone (deleted or renamed away) is noted as removed, not failed. `--require` takes a comma-separated list of `spec` and `tasks` (any case); an empty or unknown value is exit 4. See [CI hardening](ci-hardening.md#optional-verify-approvals-in-ci). |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | PASS; also a gate run whose steps were all N/A (for example `edit` with no changed C# lines) |
| 1 | FAIL |
| 2 | blocked: a hook decision, or a human-only command run by the agent |
| 3 | ERROR: the engine or a tool it ran broke (including a tool that is not on PATH). This is not a verdict about the code |
| 4 | configuration: invalid command line (unknown option, missing value), manifest or target |
| 5 | NOT-EARNED: nothing to judge, for example zero tests. Never a pass |

## Next

- [Configuration reference](configuration.md): the manifest, gate profiles and baselines these commands read
- [Plugin skills](skills.md): the skills that run these commands for you
- [Troubleshooting](troubleshooting.md): what to do when a command or a hook blocks
