---
name: init
description: Set up Lockflight in a .NET repository or a workspace shell (a root repo with Claude Code setup, Spec Kit and specs, and service repos cloned beneath it) — install and pin the engine, initialize (which adopts an existing Claude Code / Spec Kit setup: dry run first, git is the undo), make a repository a unit of a workspace shell, vendor unit CI policy, verify with doctor. Use when the user asks to install, set up, onboard, adopt or initialize Lockflight.
---

# Lockflight init

Goal: a green `doctor` with the user's own setup kept and protected. **Never run `approve`,
`baseline` or `update` yourself.** They are human decisions: the engine refuses them inside Claude Code and the guard blocks
them. Hand them to the user to run **in a separate terminal window** (not with `!` in Claude Code: that runs inside the session
and is refused), with every path written out in full.

Which engine to run: until `init` has linked the project, `~/.lockflight/bin/lockflight` (the user install). Once linked, run
every command as `.lockflight/bin/lockflight <command>` from the project root: it is the engine the hooks run.

Read a reference file next to this one only when its case applies:

- [references/adoption.md](references/adoption.md) — the project already has its own Claude Code setup (hooks in
  `.claude/settings.json`, `.claude/hooks`, skills or agents), Spec Kit (`.specify/`) or features in `specs/`.
- [references/workspace.md](references/workspace.md) — a workspace shell: child folders with their own `.git`.

## 0. Confirm the project

Run `git rev-parse --show-toplevel` (or `pwd` outside git) and ask the user to confirm this is the project to set up: show the
path and the repository's name. Change nothing until they confirm. If the directory holds `engine/src/Lockflight.Cli`, it is
Lockflight's own repository: say so and stop.

## 1. Prerequisites

Check each, and stop with the exact install command if one is missing: `dotnet --list-sdks` lists a 10.x SDK; `git`; on
Windows, Git Bash; for the standard profile, `uv` and a working `python3 -c "import yaml"`.

## 2. Engine

- **`~/.lockflight/bin/lockflight` exists** (`lockflight.exe` on Windows)**:** use it. It works whatever SDK the project's `global.json` pins.
- **It does not:** the install changes the user's home, so never run it yourself. Ask the user to open a separate terminal
  window (not `!` in Claude Code) and run `cd ~ && dotnet tool install Lockflight.Cli --prerelease --tool-path ~/.lockflight/bin`
  there (from the home directory: a project's `global.json` could pick an SDK older than 10). On Windows the install is from
  source: "Install on Windows" in docs/guides/getting-started.md. Wait until the file exists. If the install fails, point them
  to "The engine package is not found" in docs/guides/troubleshooting.md. Never suggest storing a feed token in a NuGet
  config, and never edit `nuget.config`: it is protected.
- **The project already pins a different version** (`doctor` reports the pin or link as behind, or the pin as not
  restorable): ask the user to open a separate terminal window (not `!` in Claude Code) and run
  `~/.lockflight/bin/lockflight update` there, in the project. It re-pins and links; they commit `dotnet-tools.json`.

## 3. Choose with the user

- **Profile:** **lite** (the default) — gates and guard; **standard** — adds the spec-driven lifecycle: spec and tasks in
  the spec contract, approvals, red/green with xUnit v3 or v2, review and trace.
- **Spec Kit** (standard profile only, in a project without `.specify/`): **without** (recommended unless the team uses
  Spec Kit) — the `lockflight-crew` plugin's spec writer and planner, or the session with the user, write the spec and tasks
  in the contract, with no Python needed; **with** — Spec Kit's `/speckit-*` commands and the Lockflight preset (step 4),
  which need `uv` and Python 3 with PyYAML. Say that without Spec Kit, `doctor` still reports its Lockflight preset check
  as failing in this release, and that the lifecycle commands do not depend on it. For the crew, they run
  `/plugin install lockflight-crew@lockflight`.
- **CI trigger** for `.github/workflows/lockflight-gates.yml`, which runs the `pr` gates: **pr** (every pull request and push
  to `main`), **manual** (only when started by hand) or **none** (no workflow). Recommend **pr**: the project pins a release
  from nuget.org. Only an engine built from source (a `.dev.` version) needs **manual** until the project pins a release.
- **CI platform** (`--ci-platform`): **github** (the default), or **azure**, **bitbucket** or **gitlab** when the repository
  is hosted there. Say that those three templates are not yet verified on the live services, and point to
  docs/guides/ci-other-platforms.md for making the job a required check.
- **Code quality (optional, off unless the user wants it):** `--quality` adds the recommended analyzers and the code
  metrics rules (an existing `.editorconfig` or `Directory.Build.props` gets a proposal patch the user applies);
  `--architecture-tests` adds a layer test project when the solution has `.Domain`/`.Application`/`.Infrastructure`/`.Api`
  projects. Say that the new warnings fail the build gate until the user runs `baseline build` and merges that baseline in
  its own pull request first (docs/guides/code-quality.md).

## 4. Spec Kit for a new standard project (before `init`)

Only when the user chose Spec Kit, for the standard profile in a project **without** `.specify/`. Do it before step 5: once `init` has linked the project,
Spec Kit's own files (`.claude/skills/speckit-*`) are protected, and the guard cannot see a command that writes them itself,
so you must not run one then.

- If the project has no `lockflight.yaml` yet, run from the project root:
  1. `uvx --from specify-cli==1.1.3 specify init --here --force --integration claude --script sh` (add
     `--ignore-agent-tools` if `claude` is not on PATH);
  2. `uvx --from specify-cli==1.1.3 specify preset add --dev "${CLAUDE_PLUGIN_ROOT}/speckit-preset"`.
- If `lockflight.yaml` already exists, never run them yourself: give the user both commands, with the preset as an absolute
  path (`echo "${CLAUDE_PLUGIN_ROOT}/speckit-preset"`), to run in a separate terminal window.

With an existing `.specify/`, never run `specify init`: `init --speckit-preset` upgrades it without overwriting the team's files.

## 5. Initialize

Run `~/.lockflight/bin/lockflight init --profile <lite|standard> --ci <pr|manual|none>` (plus `--ci-platform <name>` when it is
not github, and `--quality` / `--architecture-tests` when chosen) and show the
created/updated/unchanged list. It writes the manifest, the tool pin, `.gitignore`, a short Lockflight block in `CLAUDE.md`,
`.claude/settings.json` (the plugin, an `Edit` deny rule for each protected path, and the protected paths as Claude Code
sandbox write denials), the CI workflow and the linked engine; it is idempotent and only merges into existing files. In a
project with its own setup, run it with `--dry-run` first ([references/adoption.md](references/adoption.md)). Ask the user
to commit or stash their own changes before init, so its changes are one commit: the undo is `git revert` of that commit. It does not turn the sandbox on: if the user asks, point them to "The Claude Code sandbox" in
docs/guides/guardrails-and-boundaries.md, which says what .NET builds need inside it. Then follow the reference files that apply.

## 6. Verify

Run `.lockflight/bin/lockflight doctor` and show every `FAIL` and `warn` line with its fix.

Protected files are changed by the user or by `lockflight` commands, never by your edits (the full list: "Built-in protected
paths" in docs/guides/configuration.md). The guard will block you, and that is intended; never ask the user to bypass it.
When one needs changing, write the complete new file in your scratch directory, run
`.lockflight/bin/lockflight propose <path> --from <file>` and give the user the `git apply` command it prints.

## 7. Hand over

- **Commit** init's changes as one commit (to undo init later: `git revert` that commit).
- **Levi in the status line (optional).** Offer it. If the user wants it and has no status line of their own, show them the
  `statusLine` snippet from docs/guides/getting-started.md ("Levi and the status line") to add to their
  `~/.claude/settings.json` themselves. It is their settings file: never edit it for them.
- **The Stop gate is off** (`stop.gate: false`) until they turn it on, once they know how long build and tests take.
- **Known-failing tests** (standard profile) must be accepted before the first feature: the user types
  `/lockflight:baseline tests` in Claude Code, or opens a separate terminal window and runs
  `.lockflight/bin/lockflight baseline tests` there, from the project root; then they merge it in its own pull request.
