---
name: init
description: Set up Lockflight in a .NET repository or a workspace shell (a root repo with Claude Code setup, Spec Kit and specs, and service repos cloned beneath it) — install and pin the engine, initialize, adopt an existing Claude Code / Spec Kit setup (plan, user applies, undo), vendor unit CI policy, verify with doctor. Use when the user asks to install, set up, onboard, adopt or initialize Lockflight.
---

# Lockflight init

Goal: a green `doctor` with the user's own setup kept and protected. **Never run `adopt apply`, `adopt undo`, `approve` or
`baseline` yourself.** They are human decisions, the engine refuses them inside Claude Code, and the guard blocks them. Ask the
user to run them in a separate terminal window (not with `!` in Claude Code: that runs inside the session and is refused).

Which engine command to run:

- **Until `init` has linked the project**, `<lockflight>` is `~/.lockflight/bin/lockflight` when that file exists (the user
  install, from a package or from source by `scripts/dev-install.sh`; it works whatever SDK the project's `global.json` pins), otherwise
  `dotnet lockflight`.
- **Once `init` has linked it**, run every command as `.lockflight/bin/lockflight <command>` from the project root: it is the
  engine the hooks run, and it works whatever way Lockflight was installed. `dotnet lockflight` is only for CI and for
  teammates who restore the pin.

## 0. Confirm the project

Before anything else, run `git rev-parse --show-toplevel` (or `pwd` outside git) and ask the user to confirm that this is the
project to set up: show the path and the repository's name. A session is easily started in the wrong directory. Stop and
change nothing until the user confirms. If the directory holds `engine/src/Lockflight.Cli`, it is Lockflight's own repository:
say so and stop.

## 1. Prerequisites

Check each of these, and stop with the exact install command if one is missing:

- `dotnet --list-sdks` lists a 10.x SDK.
- `git` is installed.
- Windows: Git Bash is installed.
- Standard profile: `uv` is installed and `python3 -c "import yaml"` works.

If a `global.json` pins an SDK older than 10, `dotnet lockflight` cannot run there. Use `~/.lockflight/bin/lockflight` (and
`.lockflight/bin/lockflight` once linked), or ask the user to install it for their user (Install from a package in the getting-started guide, or
`scripts/dev-install.sh` from a Lockflight checkout).

## 2. Engine and plugin

- **No `~/.lockflight/bin/lockflight` and no tool pin yet:**
  1. Run `dotnet new tool-manifest` if the repository has no `dotnet-tools.json` or `.config/dotnet-tools.json`.
  2. Run `dotnet tool install Lockflight.Cli --local --prerelease`.
  3. If the package is not found (the private preview has no public feed), or a `global.json` pins an SDK older than 10, ask
     the user to install the engine for their user: `cd ~ && dotnet tool install Lockflight.Cli --prerelease --tool-path
     ~/.lockflight/bin` once the package is published, else `scripts/dev-install.sh` in a Lockflight checkout. Never suggest storing a feed token in a NuGet config, and do not edit
     `nuget.config`: it is protected.
- **`~/.lockflight/bin/lockflight` exists and the project already pins a different dev version** (`doctor` reports the pin or
  link as behind, or the pin as not restorable): ask the user to run `~/.lockflight/bin/lockflight update` in the project
  in a separate terminal window (not with `!` in Claude Code: that runs inside the session and is refused). It re-pins and
  links; they commit `dotnet-tools.json`.
- **Workspace shell** (child folders with their own `.git`): the plugin must be enabled at **user scope**. Claude Code loads
  project settings only from the directory a session starts in, so without it nothing guards a session started inside a
  service. Ask the user to run `claude plugin install lockflight@lockflight --scope user`; it changes their own settings, so
  never run it yourself. Tell them to start sessions at the shell root, where their own hooks load too.

## 3. Initialize

Choose the profile with the user:

- **lite** (the default): gates and guard;
- **standard**: adds the spec-driven lifecycle — Spec Kit, approvals, red/green with xUnit v3 or v2, review and trace.

Choose the CI trigger with the user too: `init` writes `.github/workflows/lockflight-gates.yml`, which runs the `pr` gates.

- **pr** (the default): on every pull request and every push to `main`;
- **manual**: only when started by hand, for projects that save CI minutes;
- **none**: no workflow.

Then run `<lockflight> init --profile <lite|standard> --ci <pr|manual|none>`, and show the created/updated/unchanged list.
From here on, run the engine as `.lockflight/bin/lockflight` from the project root.

- In a shell, `init` lists the services under `units:` and adds the child repositories to `.gitignore`.
- `init` is idempotent and only merges into existing files.

## 4. Adopt an existing setup

Adopt when the project already has its own Claude Code setup (hooks in `.claude/settings.json`, `.claude/hooks`, `skills`,
`agents`), Spec Kit (`.specify/`) or features in `specs/`:

1. Run `.lockflight/bin/lockflight adopt plan`, read `.lockflight/adopt/plan.md`, and explain it to the user by area:
   - **kept:** their hooks, skills and agents stay, and are now protected from you;
   - **protect:** the paths of their write-guard hooks are added to `protected:`, so shell commands are covered too;
   - **flag:**
     - a formatter that runs after edits can rewrite locked tests during a red cycle;
     - a Stop hook that builds runs next to the Stop gate;
     - a reviewer agent stays advisory, while `lockflight review` is the review of record;
   - **upgrade:** Spec Kit moves to 1.1.3 with the Lockflight preset, and their customised files are kept;
   - **mark:** existing features predate Lockflight and need no approvals;
   - **add:** units and a short block in `CLAUDE.md`.
2. Ask the user to commit or stash first, then to run **in a separate terminal window** (not with `!` in Claude Code: that
   runs inside the session and is refused):
   `cd <project root> && .lockflight/bin/lockflight adopt apply --speckit-preset <preset>`.
   - Write `<project root>` and `<preset>` out as absolute paths. The user's terminal has no `CLAUDE_PLUGIN_ROOT` and may not
     be in the project. Find the preset with `echo "${CLAUDE_PLUGIN_ROOT}/speckit-preset"`, or else
     `ls -d ~/.claude/plugins/cache/lockflight/lockflight/*/speckit-preset` (take the newest version).
   - Lite profile, or when the user wants to leave Spec Kit alone for now: `--skip-speckit` instead of `--speckit-preset`.
   It backs up first and rolls back by itself if anything fails; `.lockflight/bin/lockflight adopt undo` (the user's, in a
   separate terminal window too) later reverts what apply changed (files edited since are kept and listed).
3. When they are done, review `git diff` with them. They commit.

**Without an existing Spec Kit** (standard profile):

1. Run `uvx --from specify-cli==1.1.3 specify init --here --force --integration claude --script sh` (add
   `--ignore-agent-tools` if `claude` is not on PATH).
2. Run `uvx --from specify-cli==1.1.3 specify preset add --dev "${CLAUDE_PLUGIN_ROOT}/speckit-preset"`.

Never run `specify init` over an existing `.specify/`. Adoption upgrades it without overwriting the team's files.

## 5. Workspace shells: CI for each service

Run `.lockflight/bin/lockflight workspace sync`. It writes into each .NET service repository its CI policy:

- `lockflight.unit.yaml`;
- the engine pin;
- the service's baselines;
- the workflow.

Tell the user to review and commit in each service repository. A service's PR is judged by these files from its base branch.
Suggest a CODEOWNERS entry in each service for `lockflight.unit.yaml`, the pin and `.github/workflows/`.

## 6. Verify

Run `.lockflight/bin/lockflight doctor`, and show every `FAIL` and `warn` line with its fix. In a workspace, doctor also checks
that each unit:

- is cloned;
- has no `lockflight.yaml` of its own;
- is ignored by the shell;
- carries the current unit CI policy.

It also checks that the plugin is enabled at user scope and that no SDK pin keeps `dotnet lockflight` from running.

Protected files are changed by the user or by `lockflight` commands, never by your edits. The full list is in
docs/guides/configuration.md, "Built-in protected paths". The guard will block you, and that is intended; never ask the user
for permission to bypass it. When one needs changing, write the complete new file in your scratch directory, run
`.lockflight/bin/lockflight propose <path> --from <file>` and give the user the `git apply` command it prints.

## 7. Hand over

Tell the user:

- **Commit.** Review and commit the new files: in the shell, and in each service repository after `workspace sync`.
- **Levi in the status line (optional).** Offer it, and if the user wants it and has no status line of their own, show
  them the `statusLine` snippet from docs/guides/getting-started.md ("Levi and the status line") to add to their
  `~/.claude/settings.json` themselves. It is their settings file: never edit it for them.
- **The Stop gate is off** (`stop.gate: false`) until they turn it on. In a workspace it builds and tests only the changed
  services, at most two at a time. `doctor` names services that are slow at Stop; for those, set `units[].test.stopFilter`.
- **Known-failing tests** must be accepted before the first feature. Standard profile: for tests that already fail, the user
  runs `.lockflight/bin/lockflight baseline tests` (in a workspace, once per service with `--path <unit>`) from the project root
  in a separate terminal window (not with `!` in Claude Code: that runs inside the session and is refused), and merges it in its
  own pull request. In a workspace, a feature's tasks should name their services with `[unit:services/orders]`, so red/green
  builds only those.
