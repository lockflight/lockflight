---
name: init
description: Set up Lockflight in a .NET repository or a workspace shell (a root repo with Claude Code setup, Spec Kit and specs, and service repos cloned beneath it) — install and pin the engine, initialize, adopt an existing Claude Code / Spec Kit setup (plan, user applies, undo), vendor unit CI policy, verify with doctor. Use when the user asks to install, set up, onboard, adopt or initialize Lockflight.
---

# Lockflight init

Goal: a green `doctor` with the user's own setup kept and protected. **Never run `adopt apply`, `adopt undo`, `approve`,
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

During the preview the engine package is on no public feed, so do not start with `dotnet tool install`: it fails.

- **`~/.lockflight/bin/lockflight` exists:** use it. It works whatever SDK the project's `global.json` pins.
- **It does not:** ask the user to install the engine from source in their own terminal: clone `lockflight/lockflight` and
  run `scripts/dev-install.sh` in the checkout (it also installs the plugin at user scope). Wait until the file exists. Never
  suggest storing a feed token in a NuGet config, and never edit `nuget.config`: it is protected.
- **The project already pins a different version** (`doctor` reports the pin or link as behind, or the pin as not
  restorable): ask the user to run `~/.lockflight/bin/lockflight update` in the project, in a separate terminal window. It
  re-pins and links; they commit `dotnet-tools.json`.

## 3. Choose with the user

- **Profile:** **lite** (the default) — gates and guard; **standard** — adds the spec-driven lifecycle: Spec Kit, approvals,
  red/green with xUnit v3 or v2, review and trace.
- **CI trigger** for `.github/workflows/lockflight-gates.yml`, which runs the `pr` gates: **pr** (every pull request and push
  to `main`), **manual** (only when started by hand) or **none** (no workflow). During the preview the generated workflow
  restores an engine pin that no public feed has yet, so recommend **manual** until the package is published.

## 4. Spec Kit for a new standard project (before `init`)

Only for the standard profile in a project **without** `.specify/`. Do it before step 5: once `init` has linked the project,
Spec Kit's own files (`.claude/skills/speckit-*`) are protected, and the guard cannot see a command that writes them itself,
so you must not run one then.

- If the project has no `lockflight.yaml` yet, run from the project root:
  1. `uvx --from specify-cli==1.1.3 specify init --here --force --integration claude --script sh` (add
     `--ignore-agent-tools` if `claude` is not on PATH);
  2. `uvx --from specify-cli==1.1.3 specify preset add --dev "${CLAUDE_PLUGIN_ROOT}/speckit-preset"`.
- If `lockflight.yaml` already exists, never run them yourself: give the user both commands, with the preset as an absolute
  path (`echo "${CLAUDE_PLUGIN_ROOT}/speckit-preset"`), to run in a separate terminal window.

With an existing `.specify/`, never run `specify init`: adoption upgrades it without overwriting the team's files.

## 5. Initialize

Run `~/.lockflight/bin/lockflight init --profile <lite|standard> --ci <pr|manual|none>` and show the
created/updated/unchanged list. It writes the manifest, the tool pin, `.gitignore`, `.claude/settings.json`, the CI workflow
and the linked engine; it is idempotent and only merges into existing files. Then follow the reference files that apply.

## 6. Verify

Run `.lockflight/bin/lockflight doctor` and show every `FAIL` and `warn` line with its fix.

Protected files are changed by the user or by `lockflight` commands, never by your edits (the full list: "Built-in protected
paths" in docs/guides/configuration.md). The guard will block you, and that is intended; never ask the user to bypass it.
When one needs changing, write the complete new file in your scratch directory, run
`.lockflight/bin/lockflight propose <path> --from <file>` and give the user the `git apply` command it prints.

## 7. Hand over

- **Commit** the new files.
- **Levi in the status line (optional).** Offer it. If the user wants it and has no status line of their own, show them the
  `statusLine` snippet from docs/guides/getting-started.md ("Levi and the status line") to add to their
  `~/.claude/settings.json` themselves. It is their settings file: never edit it for them.
- **The Stop gate is off** (`stop.gate: false`) until they turn it on, once they know how long build and tests take.
- **Known-failing tests** (standard profile) must be accepted before the first feature: the user runs
  `.lockflight/bin/lockflight baseline tests` from the project root in a separate terminal window, and merges it in its own
  pull request.
