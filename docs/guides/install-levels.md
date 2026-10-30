# Install levels

Lockflight's verdict of record is the CI check: it runs under the base branch's policy at every level. What changes from
level to level is how much runs on a developer's machine, before the pull request. Choose one level per repository. The
level is not a manifest key: it is what `init` writes for Claude Code.

| | CI only | CI + native settings | CI + plugin |
|---|---|---|---|
| Command | `init --ci-only` | `init --native` | `init` (the default) |
| Manifest, engine lock, CI file | yes | yes | yes |
| `.claude/settings.json`: `Edit` deny rules and sandbox write denials for the protected paths | no | yes | yes |
| `CLAUDE.md` block | no | yes | yes |
| Guard, `TaskCompleted` and Stop gates in the session | no | yes, through the project's own hooks, when the engine is linked | yes, through the plugin's hooks |
| Skills, `/lockflight:*` commands, status line, the crew | no | no | yes |
| Engine on the developer's machine | not needed (`init --ci-only` links none) | optional: without it the hooks do nothing and the deny rules and sandbox still hold | needed: without it the plugin is in recovery mode |
| Plugin marketplace | not used | not used | `lockflight@lockflight`, offered to teammates through project settings |

## CI only

`init --ci-only` writes the manifest, the engine pin and lock, `.gitignore` entries and the CI file, and nothing for Claude
Code. It links no engine. Use it when the team's agents are not Claude Code, or when the team wants the judge without any
change to how people work. It needs a CI file, so `--ci none` is refused with it.

A person or an agent can still run the engine by hand (`lockflight gate run`, `lockflight redgreen red`). The CI check is
the boundary where it runs as a required check with policy, the lock and CODEOWNERS on the base branch
([CI hardening](ci-hardening.md)).

## CI + native settings

`init --native` writes what the plugin level writes into `.claude/settings.json`, but not the marketplace or the plugin.
The deny rules, the sandbox write denials and the `CLAUDE.md` block are the same. In their place it writes the project's own
Claude Code hooks: `SessionStart`, `PreToolUse` (every tool, blocking on a failure), `TaskCompleted` and `Stop`. Each hook runs
`.claude/hooks/lockflight-hook`, a copy of the plugin's hook shim that the engine carries and `init --native` writes. The
copy is a protected path, so the guard and the deny rules keep the agent from editing it. A `.gitattributes` rule keeps it LF
on Windows checkouts.

- **Adapter.** Each hook names its adapter in `LOCKFLIGHT_HOOK_ADAPTER=claude-code`, never as an argument (an older engine
  refuses an extra one).
- **Same checks as the plugin.** The shim runs only the engine `lockflight link` linked for this user. It checks the hash
  record `link` wrote and the hash `.lockflight/engine.lock` pins, so the native level guards exactly as the plugin level
  does.
- **No engine is fine.** With no linked engine and no lockdown, each hook does nothing, and the deny rules and the sandbox
  still apply. A teammate who installs and links the engine (`dotnet tool restore && dotnet lockflight link`, or
  `~/.lockflight/bin/lockflight engine install`) gets the guard. In a lockdown with no engine, only reading is allowed.
- **Merging is safe.** The team's own hooks and settings are kept. Lockflight finds its own hooks by the shim path in their
  command, and running `init --native` again brings them up to date in place. When `.claude/settings.json` has a `hooks` key
  that is not what Claude Code reads (not an object of lists of hook groups), init writes nothing and says why.
- **Kept up to date.** `lockflight update` refreshes the shim with the engine. `doctor` checks that the hooks and the shim are
  the ones the engine writes.
- **No slash commands.** There is no plugin at this level, so there are no `/lockflight:*` commands. A person gives approvals,
  baselines, resets and amends in a separate terminal window (`lockflight approve`, `lockflight baseline`, and so on).
- **One level at a time.** When the plugin is also enabled, in the project or in the user's settings, the guard and the Stop
  gates run twice. `init --native` and `doctor` say so. Disable the plugin for the project in `.claude/settings.local.json`
  (`"enabledPlugins": { "lockflight@lockflight": false }`) or go back to the plugin level.

Use this level when the team wants the guard without a plugin marketplace: for example, where plugins are managed centrally,
or where the hooks should follow the engine pin rather than a plugin that updates on its own.

## CI + plugin

`init` with no level option is the plugin level, described in [getting started](getting-started.md). It adds the
Lockflight marketplace and enables the plugin in the project's settings, so teammates are offered it. The plugin brings the
hooks, the skills, the `/lockflight:*` commands, the status line and, as a separate plugin, the crew.

## Moving between levels

Run `init` with the new level's option, then remove by hand what the old level wrote and the new one does not use:

- the `hooks` entries that run `.claude/hooks/lockflight-hook`, and the shim itself, when you leave the native level;
- `enabledPlugins` and `extraKnownMarketplaces` for Lockflight, when you leave the plugin level;
- `.claude/settings.json` and the `CLAUDE.md` block, when you go to CI only.

Commit the change as one commit; `git revert` undoes it.

## What each level can promise

The local controls are guardrails at every level, as described in [guardrails and boundaries](guardrails-and-boundaries.md):
the guard catches accidents, and an agent with a shell can get around a check that runs on its own machine. The boundary is
the CI check on the base branch's policy, and it is the same at all three levels.
