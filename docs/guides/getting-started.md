# Getting started

Lockflight runs deterministic checks around a Claude Code agent working in a .NET repository: a guard on every tool call, gates
at the end of a turn and in CI, and — in the standard profile — a spec-driven lifecycle with human approvals and red/green
evidence. This page takes you from nothing to a first green gate.

**Requirements:** .NET SDK 10+, git, Claude Code ≥ 2.1.295. Windows: Git Bash (the hook shim runs in it). Standard profile:
`uv` (for Spec Kit), Python 3 with PyYAML, xUnit for red/green and trace — v3 (Microsoft.Testing.Platform) or v2 (VSTest);
the test gate runs any VSTest or MTP test project.

## Install from source (no feed, no token)

For the preview: build the engine from a checkout of this repository and install it for your user.

```sh
git clone https://github.com/lockflight/lockflight && cd lockflight
scripts/dev-install.sh            # packs the engine into ~/.lockflight/feed, installs ~/.lockflight/bin/lockflight,
                                  # and installs the plugin at user scope
```

Then, in Claude Code at your project's root (the workspace shell root, or the repository root), ask *"Set up Lockflight"*. The
`lockflight:init` skill walks through the rest and stops where a step is yours. By hand it is:

```sh
cd /path/to/your/project
~/.lockflight/bin/lockflight init --profile standard   # in a shell: lists the units, ignores the child repositories
~/.lockflight/bin/lockflight adopt plan                # review .lockflight/adopt/plan.md
~/.lockflight/bin/lockflight adopt apply --speckit-preset /path/to/lockflight/plugins/lockflight/speckit-preset
                                                       # you, in your terminal; backs up first, `adopt undo` reverts it
~/.lockflight/bin/lockflight workspace sync            # a shell: each service repository gets its CI policy
~/.lockflight/bin/lockflight doctor
```

Commit in the shell, and in each service repository after `workspace sync`. Start Claude Code at the shell root.

- **Why user scope:** Claude Code loads project settings only from the directory where a session starts. With the plugin at
  user scope, the guard also runs when you start Claude inside a child repository of a workspace shell. Your own project hooks
  in the shell's `.claude/settings.json` still load only from the shell root.
- **Why `~/.lockflight/bin/lockflight` instead of `dotnet lockflight`:** the installed engine is a native binary, so it runs
  in projects whose `global.json` pins an older SDK. In such a project `dotnet lockflight` cannot run, because only SDK 10
  understands the package format.
- **Tool pin:** `init` pins the dev version, which exists only in your local feed. A teammate runs the same script to get it.
- **Updating:** run `~/.lockflight/bin/lockflight update` in a project. It pulls your checkout, reinstalls the engine and the
  plugin, re-pins and links the project and refreshes its `CLAUDE.md` block; commit what it lists. The version follows the
  commit, so nothing changes until the code does. When another project already got the newer engine, Claude Code tells you at
  session start.
- **No token on disk:** the preview needs no package-feed token. Do not add the GitHub Packages feed to your NuGet
  config with a stored token: any tool or agent that reads the file can print it, and a second package source without a
  source mapping makes `dotnet restore` warn NU1507 in every project with central package management (an error under
  `TreatWarningsAsErrors`).

## Lite profile (target: first green in under 15 minutes)

1. In Claude Code: `/plugin marketplace add lockflight/lockflight`, then `/plugin install lockflight@lockflight`.
   The engine package is not on a public feed yet: during the preview, install the engine from source as above (the
   init skill then uses `~/.lockflight/bin/lockflight`). The steps below are for when the package is published.
2. In your repository, ask Claude: *"Set up Lockflight"* (the `lockflight:init` skill). It runs:
   ```
   dotnet new tool-manifest            # when the repo has none (SDK 10 writes dotnet-tools.json at the repo root)
   dotnet tool install Lockflight.Cli --local --prerelease
   dotnet lockflight init              # manifest, tool pin, .gitignore, .claude/settings.json, CI workflow, linked engine
   dotnet lockflight doctor
   ```
   `init` pins the engine in the repository's existing tool manifest: `dotnet-tools.json` at the root, or an older
   `.config/dotnet-tools.json` when that is the only one. When the test projects target several .NET versions
   (`net8.0;net10.0`), the generated workflow installs each of them and `init` says so.
3. Commit the new files. From now on:
   - the guard blocks agent edits to protected files (`lockflight.yaml`, the tool pin, `.lockflight/`, Claude settings,
     `Directory.Build.*`, `Directory.Packages.props`, `global.json`, `NuGet.config`, code-style and analyzer config
     (`.editorconfig`, `.globalconfig`, rulesets, `stylecop.json`, `BannedSymbols.txt`), gitleaks config, the gates workflow;
     the full list is in the [configuration reference](configuration.md#built-in-protected-paths));
   - `dotnet lockflight gate run` runs format → build → tests; `--profile pr` adds vulnerable packages, secrets and — when
     `gates.coverage.collector` is set — patch coverage, as the generated CI workflow does;
   - the Stop gate is **off**; turn it on (`stop.gate: true`) once you know how long build + tests take. It gets at most
     540 s per turn (below the plugin's 600 s Stop hook timeout), whatever `gates.timeoutSeconds` says. Once it is on,
     `ledger retro` shows how many Stop gates ran, how many the cache of the last PASS answered, and their time in all.

## Standard profile

Everything above, plus the lifecycle:

1. `dotnet lockflight init --profile standard`, then (the init skill does this with you):
   ```
   uvx --from specify-cli==1.1.3 specify init --here --force --integration claude --script sh
   uvx --from specify-cli==1.1.3 specify preset add --dev <plugin>/speckit-preset
   ```
   `--force` lets Spec Kit initialise a non-empty repository without asking (it merges into existing files). Where Claude Code
   is not on `PATH` (CI, containers), add `--ignore-agent-tools`. `<plugin>` is the installed plugin's directory (under
   `~/.claude/plugins/cache/lockflight/lockflight/<version>/`); the init skill fills it in for you. The preset replaces Spec
   Kit's tasks template and appends Lockflight's rules to `/speckit-tasks`; `doctor` checks that it is installed.
   Spec Kit 1.1.3 numbers features sequentially: `specs/001-<slug>/`.
   Before the first feature, accept the tests that already fail: run `dotnet lockflight baseline tests` yourself and merge
   that baseline in its own pull request first (CI takes baselines from the base branch, so one committed with a feature is
   discarded; see [CI hardening](ci-hardening.md)). Each feature's `redgreen start` then runs the **whole suite once** to
   record the baseline and find tests that already fail — on a large suite that takes minutes (about 3 minutes for ~5,000
   test cases), so expect it and do not interrupt it.
2. Ask Claude to build a feature (the `lockflight:feature` skill). The flow:

   | Step | Who | Check |
   |---|---|---|
   | `/speckit-specify`, `/speckit-clarify` | agent + you | `lockflight phase check specify` |
   | approve the spec | **you**, in your terminal: `dotnet lockflight approve spec [--sign]` | hash-bound record |
   | `/speckit-plan`, `/speckit-tasks` | agent | `lockflight phase check tasks` |
   | approve the tasks | **you**: `dotnet lockflight approve tasks [--sign]` | hash-bound record |
   | per story: stubs → tests → `redgreen red` → implement → `redgreen green` | agent | red must fail in production code; tests locked until green |
   | `lockflight trace`, `lockflight review`, `gate run --profile pr` | agent | every requirement covered; review findings confirmed by reproductions |
   | PR and merge | **you** | CI runs the same gates with the base branch's policy |

   Approvals are refused inside Claude Code sessions on purpose: they are your decision.
3. Now and then: `dotnet lockflight ledger retro` (or ask for a retro) to see recurring incidents and suggested remedies.

## Levi and the status line

Levi, the pixel lock-keeper, shows verdicts to you and never to the agent:

- **In your terminal.** `doctor` and `gate run` draw him with the result: a green flag for `PASS`, a question for
  `NOT-EARNED`, a red flag for `FAIL`. He appears only when a person reads the output. Piped output, `NO_COLOR` and any shell
  Claude Code starts (it sets `CLAUDECODE`) get plain text, so the agent never pays tokens for him. `LOCKFLIGHT_MASCOT=0`
  turns him off.
- **In Claude Code's status line** (optional). `lockflight statusline` prints his face and the last verdict from the local
  ledger, for example `guard blocked **/.env · 2m ago`, `stop gate PASS · just now` or `Stop gate FAIL · just now`. The status line is shown to you only
  and costs no tokens. A plugin cannot set it, so add it to your user settings (`~/.claude/settings.json`) yourself, unless
  you already have a status line you want to keep:

  ```json
  {
    "statusLine": { "type": "command", "command": "~/.lockflight/bin/lockflight statusline", "refreshInterval": 5 }
  }
  ```

  Outside a Lockflight project it prints nothing.

  To keep a status line of your own, call `lockflight statusline` from your script with the same stdin and place its two
  lines next to yours. If your script runs `git status`, prefix it with `GIT_OPTIONAL_LOCKS=0`: a status line runs after
  every assistant message, and a plain `git status` briefly takes `index.lock`, which can make the agent's `git commit` fail.

## Next

- [Configuration reference](configuration.md)
- [Guardrails and boundaries](guardrails-and-boundaries.md) — what Lockflight can and cannot guarantee
- [CI hardening](ci-hardening.md) — CODEOWNERS and required checks
