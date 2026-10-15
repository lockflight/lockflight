# Troubleshooting

Start with `dotnet lockflight doctor` (or ask Claude to run the `lockflight:doctor` skill). Every `FAIL` or `warn` line has a
fix after the `→`.

## Every tool call is blocked: "engine not installed in this project"

This is **recovery mode**. The repository has a `lockflight.yaml`, but the plugin's hook shim cannot find the linked engine at
`.lockflight/bin/lockflight[.exe]`. That happens on a fresh clone or after `git clean`. Until the engine is back, the guard
allows only the read-only tools (Read, Grep, Glob, LS) and exactly one of these Bash commands, with nothing chained to them:

```sh
dotnet tool restore && dotnet lockflight link
dotnet tool restore
dotnet lockflight link
```

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
call is blocked with that command.

If the message says **"the engine pin cannot be read"**, `dotnet-tools.json` is malformed. It is protected, so fix it yourself,
then run the restore command.

## "lockflight: internal error" or "the engine failed (exit N)"

The engine broke while deciding a hook. Hooks fail closed: a tool call is blocked, while Stop and SessionStart are allowed with
the error on stderr, so a session is never bricked. Run `dotnet lockflight doctor`; if the error repeats, run the failing command
with `LOCKFLIGHT_DEBUG=1` and report it. A `LOCKFLIGHT_HOOK_LOG` that points at a path that cannot be written is reported and
skipped; it never blocks.

## A shell command is blocked after a `cd`

After a `cd` whose target the guard cannot know (`cd "$(git rev-parse --show-toplevel)"`, `cd $DIR`, `cd ~`, `cd -`, `popd`), a
relative path could be anywhere, including a protected file, so relative writes are blocked. Use a literal `cd` target
(`cd src/Shop.Api && …`) or paths from the project directory. See
[what the shell write detection sees](guardrails-and-boundaries.md#what-the-shell-write-detection-sees).

## The engine package is not found

During the private preview, `Lockflight.Cli` is served from a private GitHub Packages feed. You need a `nuget.config` that
points at the feed and a token with `read:packages`:

```sh
gh auth refresh -s read:packages
```

## A gate says NOT-EARNED

The gate could not judge, and NOT-EARNED is never a pass. The line under the step says why. Common causes:

- the test run reported no test count;
- gitleaks is not installed (set `LOCKFLIGHT_GITLEAKS` or put it on `PATH`);
- `claude` is not on `PATH` for `review`;
- red/green found no xUnit v3 test application;
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
`dotnet lockflight doctor` lists the projects that miss it.

## `redgreen green` says "other tests fail" for tests the feature never touched

`redgreen start` runs the suite once and warns when tests already fail before any change (often for environmental reasons,
such as a test that needs an older SDK). Every green is refused while they fail. If they are not the feature's business, run
`dotnet lockflight baseline tests` **in your own terminal** and merge that baseline in **its own pull request first**: CI takes
baselines from the base branch, so a baseline committed in the feature's pull request is discarded there by design
([CI hardening](ci-hardening.md)).

## `redgreen red`/`green` refuse: "approval is stale"

Approvals bind `spec.md`, `plan.md` and `tasks.md` by hash. After an edit, red/green refuses until you approve again in your
own terminal (`dotnet lockflight approve spec` or `approve tasks`). `trace` prints a warning line instead of refusing.

## Windows

The hook shim is a POSIX script, so Claude Code on Windows needs **Git Bash**. `doctor` checks for it.

## "records a human decision and does not run inside a Claude Code session"

`approve`, `baseline` and `redgreen reset` are refused when `CLAUDECODE=1` is set. That variable is set inside Claude Code
sessions, including the `!` prefix. Run these commands in a separate terminal.

## Uninstall

1. `/plugin uninstall lockflight@lockflight` in Claude Code.
2. `dotnet tool uninstall Lockflight.Cli`.
3. Remove `lockflight.yaml`, `.lockflight/`, `.github/workflows/lockflight-gates.yml` and the Lockflight entries in
   `.claude/settings.json` and `.gitignore`.
