# Troubleshooting

Fixes for the messages and blocks you are most likely to meet, grouped by what you see.

> [!TIP]
> Start with `.lockflight/bin/lockflight doctor` from the project root (or ask Claude to run the `lockflight:doctor` skill).
> Every `FAIL` or `warn` line has a fix after the `→`.

| You see | Go to |
|---|---|
| Every tool call or every write is blocked | [engine not installed](#every-tool-call-is-blocked-engine-not-installed-in-this-project) · [lockdown](#every-write-is-blocked-the-project-is-in-lockdown) · [after editing `lockflight.yaml`](#writes-are-blocked-after-editing-lockflightyaml) · [engine and pin differ](#running-engine-x-but-the-project-pins-y) · [internal error](#lockflight-internal-error-or-the-engine-failed-exit-n) |
| One command or edit is blocked | [a protected file](#the-agent-needs-to-change-a-protected-file) · [a shell command after `cd`](#a-shell-command-is-blocked-after-a-cd) · [a human decision](#records-a-human-decision-and-does-not-run-inside-a-claude-code-session) |
| Install and versions | [pinned version not found](#dotnet-lockflight-fails-the-pinned-version-is-not-found) · [development pin or plugin engine](#doctor-reports-a-development-pin-or-the-plugin-needs-a-newer-engine) · [`update` fails](#lockflight-update-fails-to-install-the-package) · [package not found](#the-engine-package-is-not-found) · [Windows](#windows) · [uninstall](#uninstall) |
| A gate result | [`NOT-EARNED`](#a-gate-says-not-earned) · [format fails](#the-format-gate-fails) · [`test: ERROR` with coverage](#test-error-with-rejected-the-coverage-options-exit-5) |
| Red/green | [other tests fail](#redgreen-green-says-other-tests-fail-for-tests-the-feature-never-touched) · [approval is stale](#redgreen-redgreen-refuse-approval-is-stale) |

## Every tool call is blocked: "engine not installed in this project"

This is **recovery mode**. The repository has a `lockflight.yaml`, but the plugin's hook shim cannot find the linked engine at
`.lockflight/bin/lockflight[.exe]`. That happens on a fresh clone or after `git clean`. Until the engine is back, the guard
allows only the read-only tools (Read, Grep, Glob, LS) and exactly one of these Bash commands, with nothing chained to them:

```sh
dotnet tool restore && dotnet lockflight link
dotnet tool restore
dotnet lockflight link
```

With an install from source the pin is a development build that no package feed has, so these commands cannot restore it.
Run `~/.lockflight/bin/lockflight link` yourself from the project root, or `~/.lockflight/bin/lockflight update` (which also
re-pins) in a separate terminal window: not with `!` in Claude Code, where `update` is refused.

## `dotnet lockflight` fails: the pinned version is not found

`doctor` reports **pin restorable** as FAIL: the project pins a development build (`0.2.0-alpha.4.dev.…`, or `0.2.0-dev.…` from older installs; from `scripts/dev-install.sh`)
that is neither in this machine's local feed (`~/.lockflight/feed`) nor in the NuGet tools cache, so `dotnet tool restore` and
`dotnet lockflight` cannot get it. Typically the pin came from a teammate's install, or from an older install whose packages were
cleaned up. The hooks are not affected while the linked engine is there, and the agent runs `.lockflight/bin/lockflight`, not
`dotnet lockflight`.

Fix: run `~/.lockflight/bin/lockflight update` in the project, in a separate terminal window, not with `!` in Claude Code (a `!` command runs inside the session and is refused). It re-pins the project to your installed engine and
links it; commit `dotnet-tools.json`. Without a user install, install the engine first ([from a package or from
source](getting-started.md)). When doctor only warns that the version is in the local feed, `dotnet tool restore --add-source ~/.lockflight/feed`
restores it.

## `doctor` reports a development pin, or the plugin needs a newer engine

**development pin** (a warning): the project pins a development build, and a release at or above the version that build
followed is installed. Development builds are on no package feed, so teammates and CI cannot restore them. Run
`~/.lockflight/bin/lockflight update` in the project, in a separate terminal window: it re-pins the project to the newest
release and links it; commit `dotnet-tools.json`.

**plugin engine** FAIL, or a session-start message "The Lockflight plugin needs engine X or newer": the plugin updated from its
marketplace, and the engine the project pins is older than the plugin's `min-engine-version`. Run the same `update`.

## `lockflight update` fails to install the package

`update` runs `dotnet tool install Lockflight.Cli` from `~/.lockflight`, so it needs the .NET 10 SDK on `PATH` and access to the
NuGet sources (nuget.org by default). Behind a mirror, pass it: `~/.lockflight/bin/lockflight update --source <feed URL>`; a
folder of `.nupkg` files works too. A failed install leaves the installed engine and the project unchanged. An engine installed
from source updates from its checkout instead and refuses `--source`; delete `~/.lockflight/source` to switch to packages.

## Every write is blocked: "the project is in lockdown"

A file `.lockflight/lockdown` exists. Lockdown is the emergency brake: while the file is there, the agent can only read.
Anyone can create the file, but only a human can remove it, because removing it is a write to a protected path. Delete the
file in your own terminal to lift the lockdown.

## Writes are blocked after editing `lockflight.yaml`

If `lockflight.yaml` doesn't parse, the hooks fall back to a read-only policy rather than guessing which rules apply. Commands
print the position of the error (`lockflight.yaml:line:column`). Fix the file yourself: it is protected, so the agent can
show you the change but cannot make it.

## "running engine X but the project pins Y"

The linked binary is older or newer than the version pinned in `dotnet-tools.json` — typically after a version bump was pulled.
A stale engine must not judge the project with old rules, so it behaves like recovery mode: the read-only tools and exactly the
restore commands above are allowed (the agent may run `dotnet tool restore && dotnet lockflight link` itself); every other tool
call is blocked with that command. For a development pin, use the commands for an install from source above.

If the message says **"the engine pin cannot be read"**, `dotnet-tools.json` is malformed. It is protected, so fix it yourself,
then run the restore command.

## "lockflight: internal error" or "the engine failed (exit N)"

The engine broke while deciding a hook. Hooks fail closed: a tool call is blocked, while Stop and SessionStart are allowed with
the error on stderr, so a session is never bricked. Run `.lockflight/bin/lockflight doctor` from the project root; if the error repeats, run the failing command
with `LOCKFLIGHT_DEBUG=1` and report it. A `LOCKFLIGHT_HOOK_LOG` that points at a path that cannot be written is reported and
skipped; it never blocks.

## The agent needs to change a protected file

The block is intended. The agent writes the complete new file outside the project and runs
`.lockflight/bin/lockflight propose <path> --from <file>` (or `<path> --delete`); you read the patch it prints and apply it with the
printed `git apply` command at the project root. See
[proposing a protected change](guardrails-and-boundaries.md#proposing-a-protected-change).

## A shell command is blocked after a `cd`

After a `cd` whose target the guard cannot know (`cd "$(git rev-parse --show-toplevel)"`, `cd $DIR`, `cd ~`, `cd -`, `popd`), a
relative path could be anywhere, including a protected file, so relative writes are blocked. Use a literal `cd` target
(`cd src/Shop.Api && …`) or paths from the project directory. See
[what the shell write detection sees](guardrails-and-boundaries.md#what-the-shell-write-detection-sees).

## The engine package is not found

The `Lockflight.Cli` packages are not on nuget.org yet, so `dotnet tool install Lockflight.Cli` finds nothing. Until they are
published, [install from source](getting-started.md#install-from-source-no-feed-no-token) with `scripts/dev-install.sh`: it
packs the engine into a local feed (`~/.lockflight/feed`) and needs no package feed and no token. [Install from a
package](getting-started.md#install-from-a-package) works once the packages are published.

A project set up from a source install pins a development build that only your machine's local feed has. `doctor` reports
that pin: **pin restorable** FAIL when this machine cannot restore it ([fix](#dotnet-lockflight-fails-the-pinned-version-is-not-found)),
or a **development pin** warning once a release is installed ([fix](#doctor-reports-a-development-pin-or-the-plugin-needs-a-newer-engine)).

## A gate says NOT-EARNED

<img src="../assets/levi-not-earned.svg" alt="Levi with a question mark" width="88" align="right">

The gate could not judge, and NOT-EARNED is never a pass. The line under the step says why. Common causes:

- the test run reported no test count;
- gitleaks is not installed (set `LOCKFLIGHT_GITLEAKS` or put it on `PATH`);
- `claude` is not on `PATH` for `review`;
- red/green found no xUnit test project (xUnit v3, or xUnit v2 on VSTest; other test frameworks get the gates only);
- `format` could not list the changed lines (with `--base`, a shallow checkout: use `fetch-depth: 0`).

A step that says **N/A** did not apply (for example `coverage.patch` without `gates.coverage.collector`). It is neither a pass
nor a failure.

## The format gate fails

The format gate judges only the lines the change touched (see the [configuration reference](configuration.md#the-format-step-judges-the-change-not-the-file)).
The detail lists `file:line (DIAGNOSTIC)`; fix those lines. `dotnet format whitespace --folder --include <file>` fixes them but
also rewrites the rest of the file, which an upstream pull request may not want.

- **"N end-of-line-only differences not attributed"**: the repository's `.editorconfig` asks for one line ending (for example
  `end_of_line = crlf`) while git checked the files out with another. This is informational; the step does not fail on it.
- **"judged whole files"**: dotnet format's report could not be matched to the files (or there is no git history to compare
  with). Every diagnostic in those files counts. Commit once so there is a baseline commit, or report the case.

## `test: ERROR` with "rejected the coverage options (exit 5)"

`gates.coverage.collector` is set, but a test project does not reference the collector's package, so `dotnet test --coverage`
(or `--coverlet`) is an unknown option and the test application exits 5. Add `Microsoft.Testing.Extensions.CodeCoverage`
(collector `microsoft`) or `coverlet.MTP` (collector `coverlet`) to every test project, or remove the setting.
`.lockflight/bin/lockflight doctor` lists the projects that miss it.

## `redgreen green` says "other tests fail" for tests the feature never touched

`redgreen start` runs the suite once and warns when tests already fail before any change (often for environmental reasons,
such as a test that needs an older SDK). Every green is refused while they fail. If they are not the feature's business, run
`.lockflight/bin/lockflight baseline tests` **in a separate terminal window** (not with `!` in Claude Code) and merge that baseline in **its own pull request first**: CI takes
baselines from the base branch, so a baseline committed in the feature's pull request is discarded there by design
([CI hardening](ci-hardening.md)).

## `redgreen red`/`green` refuse: "approval is stale"

Approvals bind `spec.md`, `plan.md` and `tasks.md` by hash. After an edit, red/green refuses until you approve again in a
separate terminal window (`.lockflight/bin/lockflight approve spec` or `approve tasks`). `trace` prints a warning line instead of refusing.

## Windows

The hook shim is a POSIX script, so Claude Code on Windows needs **Git Bash**. `doctor` checks for it.

## "records a human decision and does not run inside a Claude Code session"

`approve`, `baseline`, `redgreen reset`, `adopt apply|undo` and `update` are refused when `CLAUDECODE=1` is set. That variable
is set inside Claude Code sessions, and a command typed with the `!` prefix runs inside the session too, so it is refused as
well. Open a separate terminal window and paste the line the refusal prints: it changes to the project root and runs the same
command, for example `cd '/path/to/project' && .lockflight/bin/lockflight approve spec`.

## Uninstall

1. In each project, if you ran `adopt apply`, run `.lockflight/bin/lockflight adopt undo` first, in a separate terminal window,
   to revert what it changed.
2. In each project, remove what `init` added: `lockflight.yaml`, `.lockflight/`, the tool pin (the `lockflight.cli` entry
   in `dotnet-tools.json` or `.config/dotnet-tools.json`; `dotnet tool uninstall Lockflight.Cli` removes it),
   `.github/workflows/lockflight-gates.yml`, the Lockflight marketplace and plugin entries in `.claude/settings.json`, and the
   Lockflight blocks in `.gitignore` and (standard profile) `.gitattributes`. The `<!-- lockflight:begin -->` …
   `<!-- lockflight:end -->` block in `CLAUDE.md` can go too.
3. In Claude Code, `/plugin uninstall lockflight@lockflight`, and `/plugin uninstall lockflight-crew@lockflight` if you
   installed the crew. Then remove the marketplace: `/plugin marketplace remove lockflight`.
4. Remove the user install: `rm -rf ~/.lockflight`. A package install and an install from source share this layout
   (`~/.lockflight/bin/lockflight` pointing at `~/.lockflight/tools/<version>`); a source install also keeps its local feed
   (`~/.lockflight/feed`) and the path of its checkout (`~/.lockflight/source`) there.

## Next

- [Commands](commands.md#exit-codes): what each exit code means
- [Guardrails and boundaries](guardrails-and-boundaries.md#what-the-shell-write-detection-sees): what the guard blocks, and why
- [Report a bug](../../CONTRIBUTING.md) or [a way around a control](../../SECURITY.md)
