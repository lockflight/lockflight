# Adopting an existing setup

Read this when the project already has its own Claude Code setup (hooks in `.claude/settings.json`, `.claude/hooks`, skills,
agents), Spec Kit (`.specify/`) or features in `specs/`. Run it after `init`.

1. Run `.lockflight/bin/lockflight adopt plan`, read `.lockflight/adopt/plan.md`, and explain it to the user by area:
   - **kept:** their hooks, skills and agents stay, and are now protected from you;
   - **protect:** the paths of their write-guard hooks are added to `protected:`, so shell commands are covered too;
   - **flag:**
     - a formatter that runs after edits can rewrite locked tests during a red cycle;
     - a Stop hook that builds runs next to the Stop gate;
     - a reviewer agent stays advisory, while `lockflight review` is the review of record;
   - **Spec Kit:** what the plan says happens to it — the Lockflight preset is added and the team's customised files are
     kept and listed;
   - **mark:** existing features predate Lockflight and need no approvals;
   - **add:** units and a short block in `CLAUDE.md`.
2. Ask the user to commit or stash first, then to run **in a separate terminal window** (not with `!` in Claude Code: that
   runs inside the session and is refused):
   `cd <project root> && .lockflight/bin/lockflight adopt apply --speckit-preset <preset>`.
   - Write `<project root>` and `<preset>` out as absolute paths: the user's terminal has no `CLAUDE_PLUGIN_ROOT` and may not
     be in the project. Find the preset with `echo "${CLAUDE_PLUGIN_ROOT}/speckit-preset"`, or else
     `ls -d ~/.claude/plugins/cache/lockflight/lockflight/*/speckit-preset` (take the newest version).
   - Lite profile, or when the user wants to leave Spec Kit alone for now: `--skip-speckit` instead of `--speckit-preset`.

   Apply backs up first and rolls back by itself if anything fails. `.lockflight/bin/lockflight adopt undo` (the user's, in a
   separate terminal window too) later reverts what apply changed; files edited since are kept and listed.
3. When they are done, review `git diff` with them. They commit.

Never run `specify init` over an existing `.specify/`, and never run any Spec Kit command that writes `.claude/skills/**`
yourself once the project is linked: those files are protected.
