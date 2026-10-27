# Troubleshooting

Fixes for the messages and blocks you are most likely to meet, grouped by what you see.

> [!TIP]
> Start with `.lockflight/bin/lockflight doctor` from the project root (or ask Claude to run the `lockflight:doctor` skill).
> Every `FAIL` or `warn` line has a fix after the `→`.

| You see | Go to |
|---|---|
| Every tool call or every write is blocked | [engine not installed](#every-tool-call-is-blocked-engine-not-installed-in-this-project) · [engine not run](#every-tool-call-is-blocked-the-engine-was-not-run) · [lockdown](#every-write-is-blocked-the-project-is-in-lockdown) · [after editing `lockflight.yaml`](#writes-are-blocked-after-editing-lockflightyaml) · [engine and pin differ](#running-engine-x-but-the-project-pins-y) · [internal error](#lockflight-internal-error-or-the-engine-failed-exit-n) |
| One command or edit is blocked | [a protected file](#the-agent-needs-to-change-a-protected-file) · [a shell command after `cd`](#a-shell-command-is-blocked-after-a-cd) · [a human decision](#records-a-human-decision-and-does-not-run-inside-a-claude-code-session) |
| Install and versions | [fresh clone has no plugin](#a-fresh-clone-does-not-offer-the-lockflight-plugin) · [pinned version not found](#dotnet-lockflight-fails-the-pinned-version-is-not-found) · [development pin or plugin engine](#doctor-reports-a-development-pin-or-the-plugin-needs-a-newer-engine) · [`update` fails](#lockflight-update-fails-to-install-the-package) · [command not found](#command-not-found-lockflight) · [package not found](#the-engine-package-is-not-found) · [Windows](#windows) · [uninstall](#uninstall) |
| A gate result | [`doctor --canaries` reports MISSED or BROKEN](#doctor---canaries-reports-missed-or-broken) · [`NOT-EARNED`](#a-gate-says-not-earned) · [format fails](#the-format-gate-fails) · [`test: ERROR` with coverage](#test-error-with-rejected-the-coverage-options-exit-5) |
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

## Every tool call is blocked: the engine "was not run"

The shim found an engine at `.lockflight/bin/` but refused to run it, and is in recovery mode as above:

- **"because it is committed to the repository":** a binary in git is whatever the last committer put there, so it is never
  run. Remove it from the repository and ignore the folder, then link again:

  ```sh
  git rm --cached .lockflight/bin/lockflight    # .exe on Windows
  echo ".lockflight/bin/" >> .gitignore
  dotnet tool restore && dotnet lockflight link
  ```

- **"because its SHA-256 is not the one lockflight link recorded":** the engine at `.lockflight/bin/` is not the file `link`
  copied (or `.lockflight/bin/lockflight.sha256` was changed). Something replaced it: a copy, a build, another tool. Check what
  it was, then link again, which restores the pinned engine and its record:

  ```sh
  dotnet tool restore && dotnet lockflight link
  ```

- **"because neither sha256sum nor shasum is installed":** the shim needs one of them to compare the engine with its record.
  Install coreutils (`sha256sum`) or Perl's `shasum`.

- **"because it belongs to another user":** the `lockflight.yaml` the shim found is in a folder another account wrote (for
  example `/tmp`). Check where it came from, delete it, and link the engine in your own project.

## A fresh clone does not offer the Lockflight plugin

The repository's `.claude/settings.json` names the Lockflight marketplace, but Claude Code reads that entry
(`extraKnownMarketplaces`) only after you trust the folder. Start Claude Code at the repository root and accept the trust
dialog; it then offers the marketplace and the plugin. A `claude -p` run in a folder that was never trusted ignores the entry
without a message, so trust the folder once interactively, or install the plugin for your user
(`claude plugin marketplace add lockflight/lockflight`, then `claude plugin install lockflight@lockflight --scope user`).

## `dotnet lockflight` fails: the pinned version is not found

`doctor` reports **pin restorable** as FAIL: the project pins a development build (`0.2.0-alpha.4.dev.…`, or `0.2.0-dev.…` from older installs; from `scripts/dev-install.sh`)
that is neither in this machine's local feed (`~/.lockflight/feed`) nor in the NuGet tools cache, so `dotnet tool restore` and
`dotnet lockflight` cannot get it. Typically the pin came from a teammate's install, or from an older install whose packages were
cleaned up. The hooks are not affected while the linked engine is there, and the agent runs `.lockflight/bin/lockflight`, not
`dotnet lockflight`.

Fix: in a separate terminal window, not with `!` in Claude Code (a `!` command runs inside the session and is refused), run `~/.lockflight/bin/lockflight update` in the project. It re-pins the project to your installed engine and
links it; commit `dotnet-tools.json`. Without a user install, install the engine first ([from a package or from
source](getting-started.md)). When doctor only warns that the version is in the local feed, `dotnet tool restore --add-source ~/.lockflight/feed`
restores it.

## `doctor` reports a development pin, or the plugin needs a newer engine

**development pin** (a warning): the project pins a development build, and a release at or above the version that build
followed is installed. Development builds are on no package feed, so teammates and CI cannot restore them. In a separate
terminal window, run `~/.lockflight/bin/lockflight update` in the project: it re-pins the project to the newest
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

## `doctor` warns about gitleaks or the Claude Code version

- **gitleaks:** the `pr` profile's secrets step runs gitleaks from `PATH`, or from `LOCKFLIGHT_GITLEAKS`. Without it the step is
  NOT-EARNED, not PASS, so a local `gate run --profile pr` cannot pass. Install [gitleaks](https://github.com/gitleaks/gitleaks)
  or set `LOCKFLIGHT_GITLEAKS` to its path. The generated CI workflow downloads its own pinned copy.
- **claude code:** the plugin's hooks rely on `onFailure: block`, which Claude Code honours from 2.1.295: before it, a guard
  hook that times out lets the tool call through. Run `claude update`. `doctor` also warns when `claude --version` cannot be
  read, for example when `claude` is not on the `PATH` of the shell you run doctor in.

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

## `command not found: lockflight`

The install puts `~/.lockflight/bin` on `PATH` for **new** terminals only. Open a new terminal, or load the line now:
`source ~/.zshrc` (zsh) or `source ~/.bashrc` (bash). If it still fails:

- the install ran with `--no-modify-path`, or you use a shell whose startup file it does not know: it then wrote
  `~/.profile`, which only login shells read. Add `export PATH="$HOME/.lockflight/bin:$PATH"` to your shell's startup file,
  or run `scripts/user-path.sh ~/.lockflight/bin` from the checkout with `SHELL` set to your shell;
- on Windows, open a new terminal (a running one keeps its old `PATH`); if it is still missing, check that the user variable
  `Path` holds `%USERPROFILE%\.lockflight\bin`, or sign out and in again.

Until then, `~/.lockflight/bin/lockflight` works everywhere, and in a set-up project `.lockflight/bin/lockflight`.

## The engine package is not found

The `Lockflight.Cli` packages are not on nuget.org yet, so `dotnet tool install Lockflight.Cli` finds nothing. Until they are
published, [install from source](getting-started.md#install-from-source-no-feed-no-token) with `scripts/dev-install.sh`: it
packs the engine into a local feed (`~/.lockflight/feed`) and needs no package feed and no token. [Install from a
package](getting-started.md#install-from-a-package) works once the packages are published.

A project set up from a source install pins a development build that only your machine's local feed has. `doctor` reports
that pin: **pin restorable** FAIL when this machine cannot restore it ([fix](#dotnet-lockflight-fails-the-pinned-version-is-not-found)),
or a **development pin** warning once a release is installed ([fix](#doctor-reports-a-development-pin-or-the-plugin-needs-a-newer-engine)).

## `doctor --canaries` reports MISSED or BROKEN

- **MISSED**: a control let a planted violation through. The line names the control and what it answered. This engine build
  cannot be trusted with that control: do not rely on it, run the same command with the released engine
  (`~/.lockflight/bin/lockflight doctor --canaries`), and [report it](../../SECURITY.md) privately if the release misses it too.
- **BROKEN**: the canary could not judge. Either the harmless twin was refused as well (for example the scratch project ended up
  in lockdown, or the guard blocks every write), or, with `--canaries=full`, the generated project did not build: the line says
  which. `--canaries=full` needs the .NET SDK 10 and the xUnit packages from NuGet (or the local cache); without network, run
  `doctor --canaries` without `=full`.

## A gate says NOT-EARNED

<img src="../assets/levi-not-earned.svg" alt="Levi with a question mark" width="88" align="right">

The gate could not judge, and NOT-EARNED is never a pass. The line under the step says why. Common causes:

- the test run executed no test: none found, all skipped, or a `test.stopFilter` that matches nothing (the detail names the
  filter; fix it in `lockflight.yaml`);
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
such as a test that needs an older SDK). Every green is refused while they fail. If they are not the feature's business, type
`/lockflight:baseline tests` in Claude Code, or, **in a separate terminal window** (not with `!` in Claude Code), run
`.lockflight/bin/lockflight baseline tests`; then merge that baseline in **its own pull request first**: CI takes
baselines from the base branch, so a baseline committed in the feature's pull request is discarded there by design
([CI hardening](ci-hardening.md)).

## `redgreen red`/`green` refuse: "approval is stale"

Approvals bind `spec.md`, `plan.md` and `tasks.md` by hash. After an edit, red/green refuses until you approve again in a
separate terminal window (`.lockflight/bin/lockflight approve spec` or `approve tasks`). `trace` prints a warning line instead of refusing.

## Windows

Install with `scripts\dev-install.ps1` ([Install on Windows](getting-started.md#install-on-windows)). The hook shim is a POSIX
script, so Claude Code needs **Git Bash**; `doctor` checks for it.

- **"running scripts is disabled on this system":** PowerShell's execution policy refuses unsigned scripts. Run the installer
  as `powershell -ExecutionPolicy Bypass -File scripts\dev-install.ps1`, which changes the policy for that one run only.
- **"Git Bash was not found":** install [Git for Windows](https://git-scm.com/download/win). If it is installed somewhere
  unusual, set `CLAUDE_CODE_GIT_BASH_PATH` to its `bash.exe` (Claude Code reads the same variable).
- **"pack failed" with a linker error in `~\.lockflight\pack.log`:** the Native AOT build needs Visual Studio Build Tools with
  the *Desktop development with C++* workload. Install it, open a new terminal and run the installer again.
- **`lockflight.exe.old-…` files in `~\.lockflight\bin`:** engines that were running when an install or `update` replaced them
  (Windows does not let a running program be overwritten, only renamed). The next install or update removes them; deleting them
  by hand once no Claude Code session or terminal runs them is safe too. The same goes for `.lockflight\bin` in a project.
- **`update` says the install failed:** it runs `scripts\dev-install.ps1` in the recorded checkout (`~\.lockflight\source`)
  through Windows PowerShell; run that script by hand to see the full output, and see `~\.lockflight\pack.log` and
  `install.log`.
- **`~/.lockflight/bin/lockflight` does not run in `cmd.exe`:** `cmd` does not expand `~` or accept `/`. Use PowerShell or Git
  Bash, or `%USERPROFILE%\.lockflight\bin\lockflight.exe`.

## "records a human decision and does not run inside a Claude Code session"

`approve`, `baseline`, `redgreen reset`, `redgreen amend`, `review dismiss`, `adopt apply|undo` and `update` are refused when `CLAUDECODE=1` is set. That variable
is set inside Claude Code sessions, and a command typed with the `!` prefix runs inside the session too, so it is refused as
well. Open a separate terminal window and paste the line the refusal prints: it changes to the project root and runs the same
command, for example `cd '/path/to/project' && .lockflight/bin/lockflight approve spec`.

## Uninstall

1. In each project, if you ran `adopt apply`, first open a separate terminal window and run
   `.lockflight/bin/lockflight adopt undo` there, to revert what it changed.
2. In a standard-profile project, remove the Spec Kit preset: `uvx --from specify-cli==1.1.3 specify preset remove lockflight`.
   Spec Kit itself (`.specify/`, the `speckit-*` skills) is yours to keep or remove.
3. In each project, remove what `init` added: `lockflight.yaml`, `.lockflight/`, the tool pin (the `lockflight.cli` entry
   in `dotnet-tools.json` or `.config/dotnet-tools.json`; `dotnet tool uninstall Lockflight.Cli` removes it),
   `.github/workflows/lockflight-gates.yml`, the Lockflight marketplace and plugin entries in `.claude/settings.json`, and the
   Lockflight blocks in `.gitignore` and (standard profile) `.gitattributes`. The `<!-- lockflight:begin -->` …
   `<!-- lockflight:end -->` block in `CLAUDE.md` can go too.
4. In a workspace shell, also remove what `workspace sync` wrote into each unit repository, and commit there:
   `lockflight.unit.yaml`, the `lockflight.cli` entry in its tool manifest, `.lockflight/baselines/` and
   `.github/workflows/lockflight-gates.yml`.
5. Remove the plugins. `scripts/dev-install.sh` and the package install put them at user scope:
   `claude plugin uninstall lockflight@lockflight --scope user`, and `claude plugin uninstall lockflight-crew@lockflight --scope user`
   if you installed the crew (`/plugin uninstall …` in Claude Code does the same). Then remove the marketplace:
   `claude plugin marketplace remove lockflight`.
6. Remove the user install: `rm -rf ~/.lockflight`. A package install and an install from source share this layout
   (`~/.lockflight/bin/lockflight` pointing at `~/.lockflight/tools/<version>`, a copy on Windows: remove the folder with
   `Remove-Item -Recurse -Force ~\.lockflight` once no session runs the engine); a source install also keeps its local feed
   (`~/.lockflight/feed`) and the path of its checkout (`~/.lockflight/source`) there.
7. Take the engine off `PATH`: delete the line ending in `# added by Lockflight: its engine on PATH` from your shell's
   startup file (`~/.zshrc`, `~/.bashrc`, `~/.bash_profile` or `~/.profile`), or the file
   `~/.config/fish/conf.d/lockflight.fish`. On Windows, remove `%USERPROFILE%\.lockflight\bin` from the user variable `Path`
   (*Environment Variables*).

## Next

- [Commands](commands.md#exit-codes): what each exit code means
- [Guardrails and boundaries](guardrails-and-boundaries.md#what-the-shell-write-detection-sees): what the guard blocks, and why
- [Report a bug](../../CONTRIBUTING.md) or [a way around a control](../../SECURITY.md)
