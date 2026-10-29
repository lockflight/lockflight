# Troubleshooting

Fixes for the messages and blocks you are most likely to meet, grouped by what you see.

> [!TIP]
> Start with `.lockflight/bin/lockflight doctor` from the project root (or ask Claude to run the `lockflight:doctor` skill).
> Every `FAIL` or `warn` line has a fix after the `→`.

| You see | Go to |
|---|---|
| Every tool call or every write is blocked | [engine not installed](#every-tool-call-is-blocked-engine-not-installed-in-this-project) · [engine not run](#every-tool-call-is-blocked-the-engine-was-not-run) · [lockdown](#every-write-is-blocked-the-project-is-in-lockdown) · [after editing `lockflight.yaml`](#writes-are-blocked-after-editing-lockflightyaml) · [engine and pin differ](#running-engine-x-but-the-project-pins-y) · [internal error](#lockflight-internal-error-or-the-engine-failed-exit-n) |
| One command or edit is blocked | [a protected file](#the-agent-needs-to-change-a-protected-file) · [a human decision](#records-a-human-decision-and-does-not-run-inside-a-claude-code-session) |
| Install and versions | [fresh clone has no plugin](#a-fresh-clone-does-not-offer-the-lockflight-plugin) · [pinned version not found](#dotnet-lockflight-fails-the-pinned-version-is-not-found) · [development pin or plugin engine](#doctor-reports-a-development-pin-or-the-plugin-needs-a-newer-engine) · [`update` fails](#lockflight-update-fails-to-install-the-package) · [command not found](#command-not-found-lockflight) · [package not found](#the-engine-package-is-not-found) · [Windows](#windows) · [uninstall](#uninstall) |
| A gate result | [`doctor --self-test` reports MISSED or BROKEN](#doctor---self-test-reports-missed-or-broken) · [`NOT-EARNED`](#a-gate-says-not-earned) · [review: changes not committed](#review-says-the-working-tree-has-changes-that-are-not-committed) · [format fails](#the-format-gate-fails) · [`test: ERROR` with coverage](#test-error-with-rejected-the-coverage-options-exit-5) |
| Red/green | [other tests fail](#redgreen-green-says-other-tests-fail-for-tests-the-feature-never-touched) · [approval is stale](#redgreen-redgreen-refuse-approval-is-stale) · [a test file changed after red](#redgreen-green-or-review-says-a-test-file-changed-after-red-cycle-n-was-accepted-without-a-recorded-amend) · [amended tests do not build at red](#a-red-after-an-amend-says-the-amended-tests-do-not-build-against-the-code-as-it-was-at-red) · [a project could not be evaluated](#redgreen-or-trace-says-a-project-could-not-be-evaluated) |

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

- **"because it has no hash record":** `.lockflight/bin/lockflight.sha256` is missing or empty. The engine was linked by a
  Lockflight older than 0.2.0-alpha.7, which wrote no record, or copied in by hand. Claude Code also tells you at session
  start. An engine that old may also be older than the plugin's `min-engine-version`, and linking it again would write no
  record, so update instead: in a separate terminal window, not with `!` in Claude Code (a `!` command runs inside the
  session and is refused), run `~/.lockflight/bin/lockflight update` in the project. It installs the newest engine, re-pins
  the project to it and links it, which writes the record; commit `dotnet-tools.json`. `doctor` reports the same engine as a
  **linked engine** FAIL.

- **"because neither sha256sum nor shasum is installed":** the shim needs one of them to compare the engine with its record.
  Install coreutils (`sha256sum`) or Perl's `shasum`.

- **"because it belongs to another user":** the `lockflight.yaml` the shim found is in a folder another account wrote (for
  example `/tmp`). Check where it came from, delete it, and link the engine in your own project.

Outside Windows, the hooks make `.lockflight/bin/lockflight` read-only once they have checked it, so a copy into it fails with
"Permission denied". That is on purpose: a file written into the engine in place could keep its old modification time and go
unnoticed. Replace the engine with `dotnet lockflight link`, which puts a new file in its place.

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

## A shell write is allowed although it touches a protected file

The guard catches common accidental writes (redirections, `tee`, `cp`/`mv`, `sed -i`, `rm`, `git checkout`/`restore`,
`dotnet format`, their PowerShell equivalents). It does not follow a `cd` before a relative path, variables, `xargs`, scripts
or interpreters, and allows what it does not parse. A changed test file is still caught by its red-time hash at
`redgreen green` and at review, and a protected path by CI. See
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

`Lockflight.Cli` is published as a prerelease, so `dotnet tool install Lockflight.Cli` without `--prerelease` finds no
version. Install it as [install from a package](getting-started.md#install-from-a-package) shows: with `--prerelease`, with
the .NET 10 SDK, from your home directory rather than a project whose `global.json` pins an older SDK. Behind a mirror, add
`--add-source <feed>`. Without access to any feed, [install from source](getting-started.md#install-from-source) with
`scripts/dev-install.sh`: it packs the engine into a local feed (`~/.lockflight/feed`) and needs no package feed and no token.

A project set up from a source install pins a development build that only your machine's local feed has. `doctor` reports
that pin: **pin restorable** FAIL when this machine cannot restore it ([fix](#dotnet-lockflight-fails-the-pinned-version-is-not-found)),
or a **development pin** warning once a release is installed ([fix](#doctor-reports-a-development-pin-or-the-plugin-needs-a-newer-engine)).

## `doctor --self-test` reports MISSED or BROKEN

- **MISSED**: a control let a planted violation through. The line names the control and what it answered. This engine build
  cannot be trusted with that control: do not rely on it, run the same command with the released engine
  (`~/.lockflight/bin/lockflight doctor --self-test`), and [report it](../../SECURITY.md) privately if the release misses it too.
- **BROKEN**: the canary could not judge, because its harmless twin was refused as well (for example the scratch project ended
  up in lockdown, or the guard blocks every write): the line says which. The self-test builds nothing and needs no network or
  git.

`doctor --canaries` and `--canaries=full` are deprecated names for `--self-test`. The full canary suite, which builds a generated
project, runs in the engine's own tests (`dotnet test --filter-trait Category=Canary` in `engine/`), not in the installed engine.

## A gate says NOT-EARNED

<img src="../assets/levi-not-earned.svg" alt="Levi with a question mark" width="88" align="right">

The gate could not judge, and NOT-EARNED is never a pass. The line under the step says why. Common causes:

- the test run executed no test: none found, all skipped, or a `test.stopFilter` that matches nothing (the detail names the
  filter; fix it in `lockflight.yaml`);
- gitleaks is not installed (set `LOCKFLIGHT_GITLEAKS` or put it on `PATH`);
- `claude` is not on `PATH` for `review`;
- red/green found no test project it can drive (xUnit v3, xUnit v2 on VSTest, NUnit or MSTest; other test frameworks get the gates only);
- red/green could not list an NUnit or MSTest project's tests: the line says what the project needs,
  `Microsoft.Testing.Extensions.TrxReport` for the framework's runner on Microsoft.Testing.Platform or `Microsoft.NET.Test.Sdk`
  for VSTest;
- red/green or `trace` says a project "could not be evaluated": see [below](#redgreen-or-trace-says-a-project-could-not-be-evaluated);
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

## Red/green or trace says a project "could not be evaluated"

After the build, the engine evaluates every `.csproj` under the root (or the unit) with `dotnet msbuild -getProperty` to find
the xUnit, NUnit and MSTest test projects. When one of those evaluations fails or times out, the engine cannot tell whether the project holds
tests, and a test project left out would make the run look complete when it is not. So the step is NOT-EARNED, and the line
names the project with the first line of msbuild's error, or says it timed out.

- **Evaluation failed:** run `dotnet msbuild <project> -getProperty:IsTestProject` from the project root to see the whole error
  (a missing import, an SDK that is not installed, a `Directory.Build.props` that fails), and fix it. A project that is not meant
  to build (a leftover, a template) has to be removed or moved out of the repository: no setting excludes a project from
  discovery. A directory whose name starts with `.`, and `bin/`, `obj/` and `node_modules/`, are never searched.
- **Evaluation timed out:** all the evaluations share `gates.timeoutSeconds` (default 600). Raise it in `lockflight.yaml`, or
  find out why one project evaluates so slowly (often a restore or a network share).

A project that evaluates and simply is not a test project is never reported.

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

## `review` says the working tree has changes that are not committed

The review of record is a review of a commit. The run found files in the paths the review reads (the diff's paths and the
feature's `spec.md`) that differ from HEAD: changed or staged files, which the diff shows but no commit holds, and untracked
files, which the diff does not show at all. The run is still reviewed and recorded, and its findings count, but it is not the
review of record. Commit the files that belong to the change, stash or delete the rest (`git stash -u` also stashes untracked
files), and run `review` again. Files git ignores, `.lockflight/`, `.claude/worktrees/`, untracked `bin/` and `obj/` folders and
the other files under `specs/` never count. A local file that should never be committed belongs in `.gitignore` or
`.git/info/exclude`.

## `redgreen green` or `review` says a test file "changed after red cycle N was accepted, without a recorded amend"

The red recorded a hash of every file in the locked test projects. A file that differs now was changed, added or deleted
without an amend: by another editor, a script, or a write the guard did not recognise. If the change is wanted, a person amends
the test it belongs to (`/lockflight:amend` in the session, or `.lockflight/bin/lockflight redgreen amend <Class.Method> --reason
"…"` in a separate terminal window) and the agent runs `redgreen red`, which re-checks the test and records the new content. A
file that declares no test of the cycle (a helper, a data file) cannot be amended: restore it as it was at red (the red-time
snapshot under `refs/lockflight/red/` holds it), or discard the cycle with `redgreen reset`.

## A red after an amend says "the amended tests do not build against the code as it was at red"

The re-check builds the cycle's red-time snapshot with the amended file laid over it, so the amended test must compile against the
red-time code: the production stubs as they were when the red was accepted. An amend that uses an API added later (a renamed
member, a new parameter) cannot be re-checked that way, and neither can a build that needs a file git ignores, because ignored
files are not in the snapshot. Rewrite the amended test against the red-time API, or, as a person, discard the cycle with
`.lockflight/bin/lockflight redgreen reset` in a separate terminal window and run the red again. On Windows, a deep project can pass the 260-character path limit inside the temporary worktree (`.git/lockflight/red-…`); `git config core.longpaths true` helps git, and the .NET build needs long paths enabled in Windows. A red that says the snapshot "is
not in the repository" means its ref under `refs/lockflight/red/` was deleted; only a reset helps then.

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
